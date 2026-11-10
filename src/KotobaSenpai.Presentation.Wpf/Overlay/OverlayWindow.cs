using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Coordinates target-relative overlay presentation and the lifetime of its annotation and hover modules.</summary>
internal sealed class OverlayWindow : Window
{
    private readonly OverlayAnnotationLayer _annotations = new();
    private Canvas _canvas => _annotations.Canvas;
    private readonly OverlayDiagnostics _diagnostics;
    private readonly OverlayHoverController _hover;
    private readonly PhrasePopup _phrasePopup;
    private readonly ITargetWindowTracker? _tracker;
    private readonly ILogger? _logger;
    private readonly IRegionSelector? _regionSelector;
    private WordOverlaySession? _session;
    private bool _presentationSuspended;
    private bool IsPresentationSuppressed => _presentationSuspended || _regionSelector?.IsSelecting == true;
    private TargetWindowSnapshot? _renderedSnapshot;
    private nint _ownerHandle;
    private double _fontScale;
    private int _edgePaddingPixels = 1;

    public OverlayWindow(
        IStringLocalizer? localizer = null,
        ITargetWindowTracker? tracker = null,
        ILogger? logger = null,
        IRegionSelector? regionSelector = null)
    {
        _phrasePopup = new PhrasePopup(localizer, logger);
        _hover = new OverlayHoverController(_annotations, _phrasePopup);
        _diagnostics = new OverlayDiagnostics(logger);
        _tracker = tracker;
        _logger = logger;
        _regionSelector = regionSelector;

        if (_tracker is not null)
            _tracker.Changed += OnTargetChanged;
        if (_regionSelector is not null)
            _regionSelector.SelectionStateChanged += OnRegionSelectionChanged;
        Closed += (_, _) =>
        {
            StopHover();
            if (_tracker is not null)
                _tracker.Changed -= OnTargetChanged;
            if (_regionSelector is not null)
                _regionSelector.SelectionStateChanged -= OnRegionSelectionChanged;
        };

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        Content = _canvas;
        SourceInitialized += (_, _) =>
        {
            _phrasePopup.Owner = this;
            SetOwnerHandle();
            SetClickThrough();
            var handle = new WindowInteropHelper(this).Handle;
            OverlayWindowRegistry.Register(handle);
            Closed += (_, _) => OverlayWindowRegistry.Unregister(handle);
        };
        IsVisibleChanged += (_, _) => _diagnostics.LogWindowState(this, _ownerHandle, _session, "visibility-changed");
        StateChanged += (_, _) => _diagnostics.LogWindowState(this, _ownerHandle, _session, "window-state-changed");

        _logger?.Log(
            LogLevel.Trace,
            null,
            "[OVERLAY] Overlay window configured: topmost={0}, showActivated={1}, hitTestVisible={2}, trackerAttached={3}.",
            Topmost,
            ShowActivated,
            IsHitTestVisible,
            _tracker is not null);
    }

    internal void SetSentenceTranslationEnabled(bool enabled) => _phrasePopup.SetSentenceTranslationEnabled(enabled);

    public void Render(WordOverlaySession session, double fontScale)
    {
        _presentationSuspended = false;
        _fontScale = fontScale;
        _session = session;
        _ownerHandle = session.Target.Handle;
        _logger?.Log(
            LogLevel.Trace,
            null,
            "[OVERLAY] Render requested: sessionId={0}, targetHandle={1}, visible={2}, topmost={3}, words={4}, underlines={5}, phraseGroups={6}.",
            session.Id,
            session.Target.Handle,
            IsVisible,
            Topmost,
            session.Words.Count,
            session.Lines.Count,
            session.PhraseGroups.Count);
        if (_tracker is not null)
        {
            var snapshot = _tracker.Current;
            if (snapshot is null || snapshot.Handle != session.Target.Handle)
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[OVERLAY] Render skipped: tracker snapshot missing or handle mismatch. sessionId={0}, sessionTargetHandle={1}, snapshotHandle={2}.",
                    session.Id,
                    session.Target.Handle,
                    snapshot?.Handle.ToString() ?? "<none>");
                ClearSession();
                Hide();
                return;
            }
            if (!snapshot.IsValid)
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[OVERLAY] Render skipped and session cleared: targetHandle={0}, target is invalid.",
                    snapshot.Handle);
                ClearSession();
                Hide();
                return;
            }
            if (!snapshot.IsRenderable)
            {
                _logger?.Log(
                    LogLevel.Trace,
                    null,
                    "[OVERLAY] Render skipped and window hidden: targetHandle={0}, visible={1}, minimized={2}, foreground={3}, occluded={4}, capturable={5}, renderable={6}, overlayVisible={7}, topmost={8}, sessionId={9}, targetBounds={10}, dpi={11:0.###}.",
                    snapshot.Handle,
                    snapshot.IsVisible,
                    snapshot.IsMinimized,
                    snapshot.IsForeground,
                    snapshot.IsOccluded,
                    snapshot.IsCapturable,
                    snapshot.IsRenderable,
                    IsVisible,
                    Topmost,
                    session.Id,
                    snapshot.Bounds,
                    snapshot.DpiScale);
                StopHover();
                Hide();
                return;
            }
            session = session.Reproject(snapshot.Target);
        }

        if (IsPresentationSuppressed)
        {
            StopHover();
            Hide();
            return;
        }
        RenderCurrent(session, fontScale);
    }

    private void OnRegionSelectionChanged(object? sender, EventArgs e)
    {
        if (IsPresentationSuppressed)
        {
            StopHover();
            Hide();
        }
        else if (_session is not null)
        {
            Render(_session, _fontScale);
        }
    }

    internal void ApplyAppearance(OverlayAppearance appearance)
    {
        _phrasePopup.ApplyAppearance(appearance);
        _annotations.ApplyAppearance(appearance);
    }

    private void OnTargetChanged(object? sender, TargetWindowSnapshot snapshot)
    {
        if (_session is null)
        {
            _logger?.Log(
                LogLevel.Trace,
                null,
                "[OVERLAY] Tracker change ignored: no active overlay session. handle={0}, visible={1}, minimized={2}, foreground={3}, occluded={4}, renderable={5}.",
                snapshot.Handle,
                snapshot.IsVisible,
                snapshot.IsMinimized,
                snapshot.IsForeground,
                snapshot.IsOccluded,
                snapshot.IsRenderable);
            return;
        }

        _logger?.Log(
            LogLevel.Trace,
            null,
            "[OVERLAY] Tracker change received: sessionId={0}, handle={1}, visible={2}, minimized={3}, foreground={4}, occluded={5}, capturable={6}, renderable={7}, overlayVisible={8}, topmost={9}.",
            _session.Id,
            snapshot.Handle,
            snapshot.IsVisible,
            snapshot.IsMinimized,
            snapshot.IsForeground,
            snapshot.IsOccluded,
            snapshot.IsCapturable,
            snapshot.IsRenderable,
            IsVisible,
            Topmost);
        if (snapshot.Handle != _session.Target.Handle)
        {
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[OVERLAY] Tracker change caused overlay hide: sessionTargetHandle={0}, snapshotHandle={1}.",
                _session.Target.Handle,
                snapshot.Handle);
            ClearSession();
            Hide();
            return;
        }
        if (!snapshot.IsValid)
        {
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[OVERLAY] Tracker change cleared overlay session because target is invalid: handle={0}.",
                snapshot.Handle);
            ClearSession();
            Hide();
            return;
        }
        if (!snapshot.IsRenderable || IsPresentationSuppressed)
        {
            _logger?.Log(
                IsVisible ? LogLevel.Debug : LogLevel.Trace,
                null,
                "[OVERLAY] Tracker change caused overlay hide because presentation is suppressed or target is not renderable: handle={0}, foreground={1}, occluded={2}.",
                snapshot.Handle,
                snapshot.IsForeground,
                snapshot.IsOccluded);
            StopHover();
            Hide();
            return;
        }

        _ownerHandle = snapshot.Handle;
        // Reordering an unchanged window must not close and reopen the active hover popup.
        if (RequiresGeometryRender(_renderedSnapshot, snapshot, IsVisible))
            RenderCurrent(_session.Reproject(snapshot.Target), _fontScale);
        else
            PositionAboveTarget(snapshot.Target);
    }

    private void RenderCurrent(WordOverlaySession session, double fontScale)
    {
        _ownerHandle = session.Target.Handle;
        SetOwnerHandle();
        StopHover();
        var wasVisible = IsVisible;
        if (!IsVisible)
            Show();

        _session = session;
        var handle = new WindowInteropHelper(this).Handle;
        var scale = OverlayWindowNativeHost.ReadDpiScale(handle);
        _edgePaddingPixels = (int)Math.Ceiling(OverlayAppearanceSettings.MaximumOutlineThickness * scale);
        var edgePaddingDip = _edgePaddingPixels / scale;
        _canvas.Margin = new Thickness(edgePaddingDip);
        Width = session.Target.Bounds.Width / scale + edgePaddingDip * 2;
        Height = session.Target.Bounds.Height / scale + edgePaddingDip * 2;
        PositionAboveTarget(session.Target);
        _annotations.Render(session, scale, fontScale);
        _renderedSnapshot = _tracker?.Current;
        _hover.Start(session);

        _logger?.Log(
            LogLevel.Trace,
            null,
            "[OVERLAY] Overlay rendered: hwnd={0}, wasVisible={1}, visible={2}, topmost={3}, targetHandle={4}, targetBounds={5}, words={6}, underlines={7}, phraseGroups={8}, canvasChildren={9}.",
            handle,
            wasVisible,
            IsVisible,
            Topmost,
            session.Target.Handle,
            session.Target.Bounds,
            session.Words.Count,
            _annotations.UnderlineCount,
            session.PhraseGroups.Count,
            _canvas.Children.Count);
        _diagnostics.ScheduleAfterLayout(this, _canvas, _annotations, _fontScale, _ownerHandle, _session);
    }

    private void PositionAboveTarget(WindowTarget target)
        => OverlayWindowNativeHost.PositionAboveTarget(new WindowInteropHelper(this).Handle, target, _edgePaddingPixels, _logger);

    public void ClearSession()
    {
        _logger?.Log(
            LogLevel.Debug,
            null,
            "[OVERLAY] Session cleared: sessionId={0}, overlayVisible={1}, topmost={2}, words={3}, underlines={4}, phraseGroups={5}.",
            _session?.Id.ToString() ?? "<none>",
            IsVisible,
            Topmost,
            _session?.Words.Count ?? 0,
            _annotations.UnderlineCount,
            _session?.PhraseGroups.Count ?? 0);
        _session = null;
        _diagnostics.InvalidateScheduledLayout();
        _renderedSnapshot = null;
        _hover.ClearSession();
        _annotations.Clear();
    }

    internal void SuspendPresentation()
    {
        _presentationSuspended = true;
        StopHover();
        Hide();
    }

    private void SetOwnerHandle()
    {
        if (_ownerHandle == nint.Zero)
            return;
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != nint.Zero)
            helper.Owner = _ownerHandle;
    }

    public void StopHover() => _hover.Stop();

    internal void PollHover(int cursorX, int cursorY) => _hover.PollHover(cursorX, cursorY);

    private void SetClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        OverlayWindowNativeHost.ConfigureInput(handle, clickThrough: true);
    }

    internal static bool RequiresGeometryRender(TargetWindowSnapshot? rendered, TargetWindowSnapshot current, bool isVisible)
        => !isVisible || rendered is null
            || rendered.Handle != current.Handle
            || rendered.Bounds != current.Bounds
            || rendered.DpiScale != current.DpiScale;
}

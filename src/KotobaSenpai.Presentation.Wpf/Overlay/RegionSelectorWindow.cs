using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Presentation.Wpf.Guidance;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>
/// Interactive region-selection overlay: draggable L-shaped corner brackets bound a movable recognition sub-region, a
/// semi-transparent mask dims everything outside it, and a confirm button in the center finalizes and persists the region
/// (window-relative, normalized). Unlike the click-through word-overlay window, this one captures mouse input to drag
/// bracket arms or junctions, move the region, and click the button. Region is clamped to the window and has a minimum size.
/// </summary>
public sealed class RegionSelectorWindow : Window, IRegionSelector, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IStringLocalizer? _localizer;
    private readonly ITargetWindowTracker? _tracker;
    private readonly ITourPresentation? _guidance;
    private readonly RegionSelectionVisuals _visuals;
    private Canvas _canvas => _visuals.Canvas;
    private Button _confirmButton => _visuals.ConfirmButton;
    private readonly RegionSelectionFeedback _feedback;
    private readonly RegionSelectionDragController _drag;

    private WindowTarget? _target;
    private nint _ownerHandle;
    private RecognitionRegion _normalizedRegion = RecognitionRegion.Full;
    private ScreenRect _region;   // window-relative pixels
    private double _scale = 1.0;
    private bool _disposed;

    public RegionSelectorWindow(
        ISettingsService settings,
        IStringLocalizer? localizer = null,
        ITargetWindowTracker? tracker = null,
        ITourPresentation? guidance = null,
        Func<Border, Button, FrameworkElement>? decorateBubble = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        _tracker = tracker;
        _guidance = guidance;
        _visuals = new RegionSelectionVisuals(localizer, guidance, decorateBubble);
        _confirmButton.Click += (_, _) => Confirm();
        if (_guidance is not null) _guidance.Changed += OnGuidanceChanged;
        _feedback = new RegionSelectionFeedback(_visuals.FlashOutline, Dispatcher);
        _feedback.Completed += (_, _) => StopFlashing();
        _drag = new RegionSelectionDragController(this, _confirmButton);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Arrow;
        Content = _canvas;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                _drag.End();
            UpdateConfirmAnimation();
        };
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SourceInitialized += (_, _) =>
        {
            SetOwnerHandle();
            SetNoActivate();
            var handle = new WindowInteropHelper(this).Handle;
            OverlayWindowRegistry.Register(handle);
            // WPF clears the native handle before raising Closed.
            Closed += (_, _) => OverlayWindowRegistry.Unregister(handle);
        };
        if (_tracker is not null)
            _tracker.Changed += OnTargetChanged;
        if (_localizer is not null)
            _localizer.CultureChanged += OnCultureChanged;
        Closed += (_, _) =>
        {
            _disposed = true;
            ClearSelection();
            _feedback.Dispose();
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            if (_tracker is not null)
                _tracker.Changed -= OnTargetChanged;
            if (_localizer is not null)
                _localizer.CultureChanged -= OnCultureChanged;
            if (_guidance is not null)
            {
                _guidance.Changed -= OnGuidanceChanged;
                _guidance.Dispose();
            }
        };
        // NOTE: intentionally NOT click-through — this window must capture mouse for dragging and the confirm button.
    }

    public bool IsSelecting => _target is not null && !_feedback.IsActive;

    public void Dispose()
    {
        if (_disposed) return;
        Dispatcher.Invoke(Close);
    }

    public event EventHandler? SelectionStateChanged;

    public void Show(WindowTarget target, RecognitionRegion? initial = null)
    {
        try
        {
            ShowCore(target, initial, flashing: false);
        }
        catch
        {
            ClearSelection();
            Hide();
            throw;
        }
        if (IsSelecting)
            SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
            _ = Dispatcher.InvokeAsync(UpdateConfirmAnimation);
    }

    private void UpdateConfirmAnimation()
        => _visuals.UpdateConfirmAnimation(IsVisible && _target is not null && !_feedback.IsActive);

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnCultureChanged(sender, e));
            return;
        }
        if (_target is not null)
            Redraw();
    }

    private void ShowCore(WindowTarget target, RecognitionRegion? initial, bool flashing)
    {
        ArgumentNullException.ThrowIfNull(target);
        ClearSelection();
        var activeTarget = target;
        if (_tracker is not null)
        {
            var snapshot = _tracker.Current;
            if (snapshot is null || snapshot.Handle != target.Handle || !snapshot.IsRenderable)
            {
                Hide();
                return;
            }
            activeTarget = snapshot.Target;
        }

        _target = activeTarget;
        if (flashing) _feedback.Activate();
        _canvas.IsHitTestVisible = !flashing;
        _ownerHandle = activeTarget.Handle;
        SetOwnerHandle();
        _normalizedRegion = initial ?? RecognitionRegion.Full;
        _region = _normalizedRegion.ToPixelRect(activeTarget.Bounds.Width, activeTarget.Bounds.Height);

        if (!IsVisible)
            Show();
        SetNoActivate();
        PositionAndRedraw();
        if (!flashing) _guidance?.Start();
        if (flashing)
            _feedback.Start();
    }

    public void Flash(WindowTarget target, RecognitionRegion? region = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Flash(target, region));
            return;
        }
        // Keep an active editor intact when an asynchronous recognition finishes.
        if (IsVisible && !_feedback.IsActive)
            return;
        ShowCore(target, region, flashing: true);
    }

    public void StopFlashing()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(StopFlashing);
            return;
        }
        if (!_feedback.IsActive)
            return;
        ClearSelection();
        Hide();
    }

    private void OnTargetChanged(object? sender, TargetWindowSnapshot snapshot)
    {
        if (_target is null)
            return;
        if (snapshot.Handle != _target.Handle)
        {
            ClearSelection();
            Hide();
            return;
        }
        if (!snapshot.IsValid)
        {
            ClearSelection();
            Hide();
            return;
        }
        if (!snapshot.IsRenderable)
        {
            if (_feedback.IsActive)
            {
                StopFlashing();
                return;
            }
            _drag.End();
            if (IsVisible)
                Hide();
            return;
        }

        if (_target.Bounds != snapshot.Bounds)
            _drag.End();
        _target = snapshot.Target;
        _ownerHandle = snapshot.Handle;
        SetOwnerHandle();
        _region = _normalizedRegion.ToPixelRect(_target.Bounds.Width, _target.Bounds.Height);
        if (!IsVisible)
            Show();
        PositionAndRedraw();
    }

    private void ClearSelection()
    {
        var wasSelecting = IsSelecting;
        _feedback.Stop();
        _canvas.Children.Clear();
        _canvas.IsHitTestVisible = true;
        SetNoActivate();
        _target = null;
        _guidance?.Stop();
        _drag.End();
        UpdateConfirmAnimation();
        if (wasSelecting)
            SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        ClearSelection();
        Hide();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_target is null || _feedback.IsActive || _guidance?.Target is not null || _confirmButton.IsMouseOver)
            return;
        e.Handled = _drag.Begin(e.GetPosition(_canvas), _region, _scale);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_target is null || _feedback.IsActive || _guidance?.Target is not null)
            return;
        var p = e.GetPosition(_canvas);
        if (_drag.Move(p, e.LeftButton, _scale, _target.Bounds.Width, _target.Bounds.Height) is { } updated)
        {
            _region = updated;
            _normalizedRegion = RecognitionRegion.FromPixelRect(_region, _target.Bounds.Width, _target.Bounds.Height);
            Redraw();
        }
        UpdatePointerCursor(p);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _drag.End();
        if (_target is not null && !_feedback.IsActive)
            UpdatePointerCursor(e.GetPosition(_canvas));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _drag.CaptureLost();
        if (_target is not null && !_feedback.IsActive)
            UpdatePointerCursor(Mouse.GetPosition(_canvas));
    }

    private void UpdatePointerCursor(Point point)
    {
        if (_guidance?.Target is not null) { Cursor = Cursors.Arrow; return; }
        _drag.UpdateCursor(point, _region, _scale);
    }

    private void PositionAndRedraw()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var scale = OverlayWindowNativeHost.ReadDpiScale(handle);
        if (_scale != scale)
            _drag.End();
        _scale = scale;
        Width = _target!.Bounds.Width / _scale;
        Height = _target.Bounds.Height / _scale;
        OverlayWindowNativeHost.PositionAboveTarget(handle, _target);
        Redraw();
    }

    private void Redraw() => _visuals.Render(_target!, _region, _scale, _feedback.IsActive);

    private void OnGuidanceChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnGuidanceChanged(sender, e));
            return;
        }
        if (_target is not null && !_feedback.IsActive) _visuals.DrawGuidance();
    }

    private void Confirm()
    {
        if (_target is null || _guidance?.Target is not null)
            return;
        var normalized = RecognitionRegion.FromPixelRect(
            _region, _target.Bounds.Width, _target.Bounds.Height);
        _settings.SetValue(RecognitionRegion.SettingsKey, normalized.Serialize());
        ClearSelection();
        Hide();
    }

    private void SetNoActivate()
        => OverlayWindowNativeHost.ConfigureInput(new WindowInteropHelper(this).Handle, clickThrough: _feedback.IsActive);

    private void SetOwnerHandle()
    {
        if (_ownerHandle == nint.Zero)
            return;
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != nint.Zero)
            helper.Owner = _ownerHandle;
    }
}

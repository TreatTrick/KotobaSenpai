using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private Grid _flashOutline => _visuals.FlashOutline;
    private readonly DispatcherTimer _flashTimer;

    private WindowTarget? _target;
    private nint _ownerHandle;
    private RecognitionRegion _normalizedRegion = RecognitionRegion.Full;
    private ScreenRect _region;   // window-relative pixels
    private double _scale = 1.0;
    private RegionDragMode _dragMode;
    private Point _dragStart;
    private ScreenRect _dragRegion;
    private bool _isFlashing;
    private int _flashesRemaining;
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
        _flashTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _flashTimer.Tick += OnFlashTick;

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
                EndDrag();
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

    public bool IsSelecting => _target is not null && !_isFlashing;

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
        => _visuals.UpdateConfirmAnimation(IsVisible && _target is not null && !_isFlashing);

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
        _isFlashing = flashing;
        _flashesRemaining = 5;
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
            _flashTimer.Start();
    }

    public void Flash(WindowTarget target, RecognitionRegion? region = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Flash(target, region));
            return;
        }
        // Keep an active editor intact when an asynchronous recognition finishes.
        if (IsVisible && !_isFlashing)
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
        if (!_isFlashing)
            return;
        ClearSelection();
        Hide();
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        if (_flashOutline.Visibility == Visibility.Visible)
        {
            _flashOutline.Visibility = Visibility.Hidden;
            if (--_flashesRemaining == 0)
                StopFlashing();
        }
        else
        {
            _flashOutline.Visibility = Visibility.Visible;
        }
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
            if (_isFlashing)
            {
                StopFlashing();
                return;
            }
            EndDrag();
            if (IsVisible)
                Hide();
            return;
        }

        if (_target.Bounds != snapshot.Bounds)
            EndDrag();
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
        _flashTimer.Stop();
        _isFlashing = false;
        _canvas.Children.Clear();
        _canvas.IsHitTestVisible = true;
        _flashOutline.Visibility = Visibility.Visible;
        SetNoActivate();
        _target = null;
        _guidance?.Stop();
        EndDrag();
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
        if (_target is null || _isFlashing || _guidance?.Target is not null || _confirmButton.IsMouseOver)
            return;
        var p = e.GetPosition(_canvas);
        _dragMode = RegionSelectionGeometry.HitTest(_region, _scale, p.X, p.Y);
        if (_dragMode == RegionDragMode.None)
            return;
        _dragStart = new Point(p.X * _scale, p.Y * _scale);
        _dragRegion = _region;
        if (CaptureMouse())
        {
            UpdatePointerCursor(p);
            e.Handled = true;
        }
        else
            EndDrag();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_target is null || _isFlashing || _guidance?.Target is not null)
            return;
        var p = e.GetPosition(_canvas);
        if (_dragMode != RegionDragMode.None)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                EndDrag();
            else
            {
                var current = new Point(p.X * _scale, p.Y * _scale);
                _region = RegionSelectionGeometry.Drag(_dragRegion, _dragMode, current.X - _dragStart.X, current.Y - _dragStart.Y,
                    _target.Bounds.Width, _target.Bounds.Height);
                _normalizedRegion = RecognitionRegion.FromPixelRect(_region, _target.Bounds.Width, _target.Bounds.Height);
                Redraw();
            }
        }
        UpdatePointerCursor(p);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
        if (_target is not null && !_isFlashing)
            UpdatePointerCursor(e.GetPosition(_canvas));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragMode = RegionDragMode.None;
        if (_target is not null && !_isFlashing)
            UpdatePointerCursor(Mouse.GetPosition(_canvas));
    }

    private void EndDrag()
    {
        _dragMode = RegionDragMode.None;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void UpdatePointerCursor(Point point)
    {
        if (_guidance?.Target is not null) { Cursor = Cursors.Arrow; return; }
        if (_dragMode == RegionDragMode.None && _confirmButton.IsMouseOver)
        {
            Cursor = Cursors.Hand;
            return;
        }
        var mode = _dragMode == RegionDragMode.None
            ? RegionSelectionGeometry.HitTest(_region, _scale, point.X, point.Y) : _dragMode;
        Cursor = mode switch
        {
            RegionDragMode.Left or RegionDragMode.Right => Cursors.SizeWE,
            RegionDragMode.Top or RegionDragMode.Bottom => Cursors.SizeNS,
            RegionDragMode.TopLeft or RegionDragMode.BottomRight => Cursors.SizeNWSE,
            RegionDragMode.TopRight or RegionDragMode.BottomLeft => Cursors.SizeNESW,
            RegionDragMode.Move => Cursors.SizeAll,
            _ => Cursors.Arrow,
        };
    }

    private void PositionAndRedraw()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var scale = OverlayWindowNativeHost.ReadDpiScale(handle);
        if (_scale != scale)
            EndDrag();
        _scale = scale;
        Width = _target!.Bounds.Width / _scale;
        Height = _target.Bounds.Height / _scale;
        OverlayWindowNativeHost.PositionAboveTarget(handle, _target);
        Redraw();
    }

    private void Redraw() => _visuals.Render(_target!, _region, _scale, _isFlashing);

    private void OnGuidanceChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnGuidanceChanged(sender, e));
            return;
        }
        if (_target is not null && !_isFlashing) _visuals.DrawGuidance();
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
        => OverlayWindowNativeHost.ConfigureInput(new WindowInteropHelper(this).Handle, clickThrough: _isFlashing);

    private void SetOwnerHandle()
    {
        if (_ownerHandle == nint.Zero)
            return;
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != nint.Zero)
            helper.Owner = _ownerHandle;
    }
}

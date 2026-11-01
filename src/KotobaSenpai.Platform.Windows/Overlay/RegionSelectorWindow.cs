using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// Interactive region-selection overlay: draggable L-shaped corner brackets bound a movable recognition sub-region, a
/// semi-transparent mask dims everything outside it, and a confirm button in the center finalizes and persists the region
/// (window-relative, normalized). Unlike the click-through word-overlay window, this one captures mouse input to drag
/// bracket arms or junctions, move the region, and click the button. Region is clamped to the window and has a minimum size.
/// </summary>
public sealed class RegionSelectorWindow : Window, IRegionSelector
{
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
    private static readonly Brush HandleBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x00));

    private readonly ISettingsService _settings;
    private readonly IStringLocalizer? _localizer;
    private readonly ITargetWindowTracker? _tracker;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Button _confirmButton;
    private readonly RotateTransform _confirmBorderRotation = new(0, 0.5, 0.5);
    private readonly LinearGradientBrush _confirmBorderBrush;
    private readonly Grid _flashOutline = CreateWarningOutline();
    private readonly TextBlock _feedbackText = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        LineHeight = 24,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x79, 0x89)),
    };
    private readonly Border _feedbackMessage;
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

    public RegionSelectorWindow(
        ISettingsService settings,
        IStringLocalizer? localizer = null,
        ITargetWindowTracker? tracker = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        _tracker = tracker;
        _confirmBorderBrush = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0xEF, 0x47, 0x6F), 0),
            new(Color.FromRgb(0xFF, 0x9F, 0x1C), 0.17),
            new(Color.FromRgb(0xFF, 0xD1, 0x66), 0.33),
            new(Color.FromRgb(0x06, 0xD6, 0xA0), 0.5),
            new(Color.FromRgb(0x11, 0x8A, 0xB2), 0.67),
            new(Color.FromRgb(0xA8, 0x55, 0xF7), 0.83),
            new(Color.FromRgb(0xEF, 0x47, 0x6F), 1),
        }, new Point(0, 0), new Point(1, 1)) { RelativeTransform = _confirmBorderRotation };
        _confirmButton = CreateConfirmButton();
        _confirmButton.Click += (_, _) => Confirm();
        _feedbackMessage = CreateWarningMessage(_feedbackText);
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
        Cursor = Cursors.SizeAll;
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
            ClearSelection();
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            if (_tracker is not null)
                _tracker.Changed -= OnTargetChanged;
            if (_localizer is not null)
                _localizer.CultureChanged -= OnCultureChanged;
        };
        // NOTE: intentionally NOT click-through — this window must capture mouse for dragging and the confirm button.
    }

    public void Show(WindowTarget target, RecognitionRegion? initial = null)
        => ShowCore(target, initial, flashing: false);

    private static Button CreateConfirmButton()
    {
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, 0.9));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(OpacityProperty, 0.8));
        template.Triggers.Add(pressed);
        return new Button
        {
            Padding = new Thickness(14, 6, 14, 6),
            BorderThickness = new Thickness(2.5),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Template = template,
        };
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
            _ = Dispatcher.InvokeAsync(UpdateConfirmAnimation);
    }

    private void UpdateConfirmAnimation()
    {
        _confirmBorderRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _confirmBorderRotation.Angle = 0;
        _confirmButton.Background = SystemParameters.HighContrast
            ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0));
        _confirmButton.Foreground = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brushes.White;
        _confirmButton.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : _confirmBorderBrush;
        if (!IsVisible || _target is null || _isFlashing
            || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;
        _confirmBorderRotation.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static Grid CreateWarningOutline()
    {
        var outline = new Grid { IsHitTestVisible = false };
        outline.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x50)),
            StrokeThickness = 3,
            RadiusX = 6,
            RadiusY = 6,
            Margin = new Thickness(2),
            Effect = CreateWarningGlow(16, 0.95),
        });
        outline.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0xDC)),
            StrokeThickness = 1,
            RadiusX = 5,
            RadiusY = 5,
            Margin = new Thickness(3),
        });
        return outline;
    }

    private static DropShadowEffect CreateWarningGlow(double blurRadius, double opacity)
    {
        var glow = new DropShadowEffect
        {
            Color = Color.FromRgb(0xFF, 0x2A, 0x40),
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Opacity = opacity,
            RenderingBias = RenderingBias.Quality,
        };
        glow.Freeze();
        return glow;
    }

    private static Border CreateWarningMessage(TextBlock text)
    {
        var pen = new Pen(text.Foreground, 1.75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(12, 12), 10, 10)));
        drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M8,8 L16,16 M16,8 L8,16")));
        drawing.Freeze();
        var content = new Grid { Effect = CreateWarningGlow(8, 0.8) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new Image
        {
            Source = new DrawingImage(drawing), Width = 24, Height = 24,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return new Border
        {
            Child = content, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0x19, 0x1C, 0x24)), IsHitTestVisible = false,
        };
    }

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
        _flashTimer.Stop();
        _isFlashing = false;
        _canvas.Children.Clear();
        _canvas.IsHitTestVisible = true;
        _flashOutline.Visibility = Visibility.Visible;
        SetNoActivate();
        _target = null;
        EndDrag();
        UpdateConfirmAnimation();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_target is null || _isFlashing || _confirmButton.IsMouseOver)
            return;
        var p = e.GetPosition(_canvas);
        _dragMode = RegionSelectionGeometry.HitTest(_region, _scale, p);
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
        if (_target is null || _isFlashing)
            return;
        var p = e.GetPosition(_canvas);
        if (_dragMode != RegionDragMode.None)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                EndDrag();
            else
            {
                var current = new Point(p.X * _scale, p.Y * _scale);
                _region = RegionSelectionGeometry.Drag(_dragRegion, _dragMode, current - _dragStart,
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
        Cursor = Cursors.SizeAll;
    }

    private void UpdatePointerCursor(Point point)
    {
        if (_dragMode == RegionDragMode.None && _confirmButton.IsMouseOver)
        {
            Cursor = Cursors.Hand;
            return;
        }
        var mode = _dragMode == RegionDragMode.None
            ? RegionSelectionGeometry.HitTest(_region, _scale, point) : _dragMode;
        Cursor = mode switch
        {
            RegionDragMode.Left or RegionDragMode.Right => Cursors.SizeWE,
            RegionDragMode.Top or RegionDragMode.Bottom => Cursors.SizeNS,
            RegionDragMode.TopLeft or RegionDragMode.BottomRight => Cursors.SizeNWSE,
            RegionDragMode.TopRight or RegionDragMode.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.SizeAll,
        };
    }

    private void PositionAndRedraw()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = NativeMethods.GetDpiForWindow(handle);
        var scale = (dpi == 0 ? 96 : dpi) / 96d;
        if (_scale != scale)
            EndDrag();
        _scale = scale;
        Width = _target!.Bounds.Width / _scale;
        Height = _target.Bounds.Height / _scale;
        var insertAfter = NativeMethods.GetWindow(_target!.Handle, NativeMethods.GwHwndPrev);
        var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow;
        if (insertAfter == handle)
        {
            insertAfter = nint.Zero;
            flags |= NativeMethods.SwpNoZOrder;
        }
        NativeMethods.SetWindowPos(
            handle,
            insertAfter,
            _target.Bounds.X,
            _target.Bounds.Y,
            0,
            0,
            flags | NativeMethods.SwpNoSize);
        Redraw();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        var target = _target!;
        double winW = target.Bounds.Width / _scale;
        double winH = target.Bounds.Height / _scale;
        double sx = _region.X / _scale, sy = _region.Y / _scale;
        double sw = _region.Width / _scale, sh = _region.Height / _scale;

        if (_isFlashing)
        {
            _flashOutline.Width = sw;
            _flashOutline.Height = sh;
            Canvas.SetLeft(_flashOutline, sx);
            Canvas.SetTop(_flashOutline, sy);
            _canvas.Children.Add(_flashOutline);
            _feedbackText.Text = _localizer?.Get("Region_NoJapanese")
                ?? "No recognizable Japanese was found in this area. Please select a different area.";
            _feedbackMessage.MaxWidth = Math.Min(560, Math.Min(Math.Max(0, winW - 24), Math.Max(280, sw - 32)));
            _feedbackMessage.MaxHeight = Math.Max(0, winH - 24);
            _feedbackMessage.Measure(new Size(_feedbackMessage.MaxWidth, _feedbackMessage.MaxHeight));
            var messageSize = _feedbackMessage.DesiredSize;
            Canvas.SetLeft(_feedbackMessage, Math.Clamp(sx + (sw - messageSize.Width) / 2,
                12, Math.Max(12, winW - messageSize.Width - 12)));
            Canvas.SetTop(_feedbackMessage, Math.Clamp(sy + (sh - messageSize.Height) / 2,
                12, Math.Max(12, winH - messageSize.Height - 12)));
            _canvas.Children.Add(_feedbackMessage);
            return;
        }

        // Dim the four areas outside the region so the capture range stands out.
        AddRect(0, 0, winW, sy);
        AddRect(0, sy + sh, winW, winH - sy - sh);
        AddRect(0, sy, sx, sh);
        AddRect(sx + sw, sy, winW - sx - sw, sh);

        // Four L-shaped corner brackets.
        DrawHandle(sx, sy, +1, +1);
        DrawHandle(sx + sw, sy, -1, +1);
        DrawHandle(sx, sy + sh, +1, -1);
        DrawHandle(sx + sw, sy + sh, -1, -1);

        // Reuse the button so geometry updates do not restart its rainbow animation.
        _confirmButton.Content = _localizer?.Get("Region_Confirm") ?? "OK";
        AutomationProperties.SetName(_confirmButton, (string)_confirmButton.Content);
        _confirmButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_confirmButton, sx + sw / 2 - _confirmButton.DesiredSize.Width / 2);
        Canvas.SetTop(_confirmButton, sy + sh / 2 - _confirmButton.DesiredSize.Height / 2);
        _canvas.Children.Add(_confirmButton);
    }

    private void AddRect(double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0)
            return;
        var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = DimBrush };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _canvas.Children.Add(rect);
    }

    private void DrawHandle(double cx, double cy, int dirX, int dirY)
    {
        var horizontalLength = Math.Min(RegionSelectionGeometry.ArmLength, _region.Width / _scale / 2);
        var verticalLength = Math.Min(RegionSelectionGeometry.ArmLength, _region.Height / _scale / 2);
        var thickness = RegionSelectionGeometry.ArmThickness;
        AddArm(cx, dirY < 0 ? cy - thickness : cy, horizontalLength * dirX, thickness);
        AddArm(dirX < 0 ? cx - thickness : cx, cy, thickness, verticalLength * dirY);
    }

    private void AddArm(double x, double y, double w, double h)
    {
        var arm = new System.Windows.Shapes.Rectangle
        {
            Width = Math.Abs(w),
            Height = Math.Abs(h),
            Fill = HandleBrush,
            RadiusX = 1,
            RadiusY = 1,
        };
        Canvas.SetLeft(arm, w < 0 ? x + w : x);
        Canvas.SetTop(arm, h < 0 ? y + h : y);
        _canvas.Children.Add(arm);
    }

    private void Confirm()
    {
        if (_target is null)
            return;
        var normalized = RecognitionRegion.FromPixelRect(
            _region, _target.Bounds.Width, _target.Bounds.Height);
        _settings.SetValue(RecognitionRegion.SettingsKey, normalized.Serialize());
        ClearSelection();
        Hide();
    }

    private void SetNoActivate()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
            return;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        style = _isFlashing
            ? style | NativeMethods.WsExTransparent
            : style & ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
            new nint(style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
    }

    private void SetOwnerHandle()
    {
        if (_ownerHandle == nint.Zero)
            return;
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != nint.Zero)
            helper.Owner = _ownerHandle;
    }

    private static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const long WsExNoActivate = 0x08000000;
        public const long WsExTransparent = 0x20;
        public const long WsExToolWindow = 0x80;
        public const uint GwHwndPrev = 3;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpShowWindow = 0x0040;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern nint GetWindow(nint hWnd, uint command);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    }
}

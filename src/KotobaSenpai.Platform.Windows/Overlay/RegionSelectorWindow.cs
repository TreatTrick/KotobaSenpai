using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// Interactive region-selection overlay: four draggable L-shaped corner brackets bound a recognition sub-region, a
/// semi-transparent mask dims everything outside it, and a confirm button in the center finalizes and persists the region
/// (window-relative, normalized). Unlike the click-through word-overlay window, this one captures mouse input to drag
/// corners and click the button. Region is clamped to the window and never drops below a minimum size.
/// </summary>
public sealed class RegionSelectorWindow : Window, IRegionSelector
{
    private const double HitRadiusPx = 24;
    private const double ArmLength = 26;
    private const double ArmThickness = 5;

    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
    private static readonly Brush HandleBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x00));

    private readonly ISettingsService _settings;
    private readonly IStringLocalizer? _localizer;
    private readonly ITargetWindowTracker? _tracker;
    private readonly Canvas _canvas = new();

    private WindowTarget? _target;
    private RecognitionRegion _normalizedRegion = RecognitionRegion.Full;
    private ScreenRect _region;   // window-relative pixels
    private double _scale = 1.0;
    private int _dragCorner = -1; // 0..3 = TL,TR,BL,BR; -1 = none

    public RegionSelectorWindow(
        ISettingsService settings,
        IStringLocalizer? localizer = null,
        ITargetWindowTracker? tracker = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer;
        _tracker = tracker;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = _canvas;
        SourceInitialized += (_, _) => SetNoActivate();
        if (_tracker is not null)
            _tracker.Changed += OnTargetChanged;
        Closed += (_, _) =>
        {
            if (_tracker is not null)
                _tracker.Changed -= OnTargetChanged;
        };
        // NOTE: intentionally NOT click-through — this window must capture mouse for dragging and the confirm button.
    }

    public void Show(WindowTarget target, RecognitionRegion? initial = null)
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
        _normalizedRegion = initial ?? RecognitionRegion.Full;
        _region = _normalizedRegion.ToPixelRect(activeTarget.Bounds.Width, activeTarget.Bounds.Height);

        if (!IsVisible)
            Show();
        PositionAndRedraw();
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
        if (!snapshot.IsRenderable)
        {
            _dragCorner = -1;
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            if (IsVisible)
                Hide();
            return;
        }

        _target = snapshot.Target;
        _region = _normalizedRegion.ToPixelRect(_target.Bounds.Width, _target.Bounds.Height);
        if (!IsVisible)
            Show();
        PositionAndRedraw();
    }

    private void ClearSelection()
    {
        _target = null;
        _dragCorner = -1;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(_canvas);
        _dragCorner = HitCorner(p.X * _scale, p.Y * _scale);
        if (_dragCorner >= 0)
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragCorner < 0 || _target is null)
            return;
        var p = e.GetPosition(_canvas);
        UpdateCorner(_dragCorner, p.X * _scale, p.Y * _scale);
        Redraw();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragCorner = -1;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void PositionAndRedraw()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = NativeMethods.GetDpiForWindow(handle);
        _scale = (dpi == 0 ? 96 : dpi) / 96d;
        Width = _target!.Bounds.Width / _scale;
        Height = _target.Bounds.Height / _scale;
        NativeMethods.SetWindowPos(
            handle,
            nint.Zero,
            _target.Bounds.X,
            _target.Bounds.Y,
            0,
            0,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
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

        // Confirm button, centered in the region.
        var button = new Button
        {
            Content = _localizer?.Get("Region_Confirm") ?? "OK",
            Padding = new Thickness(14, 6, 14, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
        };
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(button, sx + sw / 2 - button.DesiredSize.Width / 2);
        Canvas.SetTop(button, sy + sh / 2 - button.DesiredSize.Height / 2);
        button.Click += (_, _) => Confirm();
        _canvas.Children.Add(button);
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
        AddArm(cx, cy, ArmLength * dirX, ArmThickness);   // horizontal arm
        AddArm(cx, cy, ArmThickness, ArmLength * dirY);    // vertical arm
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

    /// <summary>Returns the corner index (0=TL,1=TR,2=BL,3=BR) within the hit radius of the cursor, or -1.</summary>
    private int HitCorner(double px, double py)
    {
        var corners = new[] { (0, _region.X, _region.Y), (1, _region.Right, _region.Y), (2, _region.X, _region.Bottom), (3, _region.Right, _region.Bottom) };
        foreach (var (index, x, y) in corners)
        {
            if (Math.Abs(px - x) <= HitRadiusPx && Math.Abs(py - y) <= HitRadiusPx)
                return index;
        }
        return -1;
    }

    private void UpdateCorner(int corner, double px, double py)
    {
        var target = _target!;
        int winW = target.Bounds.Width, winH = target.Bounds.Height;
        int minW = Math.Max(1, (int)Math.Round(winW * RecognitionRegion.MinFraction));
        int minH = Math.Max(1, (int)Math.Round(winH * RecognitionRegion.MinFraction));
        int x = (int)Math.Round(Math.Clamp(px, 0, winW));
        int y = (int)Math.Round(Math.Clamp(py, 0, winH));

        int left = _region.X, right = _region.Right, top = _region.Y, bottom = _region.Bottom;
        switch (corner)
        {
            case 0: left = Math.Min(x, right - minW); top = Math.Min(y, bottom - minH); break;
            case 1: right = Math.Max(x, left + minW); top = Math.Min(y, bottom - minH); break;
            case 2: left = Math.Min(x, right - minW); bottom = Math.Max(y, top + minH); break;
            default: right = Math.Max(x, left + minW); bottom = Math.Max(y, top + minH); break;
        }
        left = Math.Clamp(left, 0, winW);
        top = Math.Clamp(top, 0, winH);
        right = Math.Clamp(right, left, winW);
        bottom = Math.Clamp(bottom, top, winH);
        _region = new ScreenRect(left, top, right - left, bottom - top);
        _normalizedRegion = RecognitionRegion.FromPixelRect(_region, winW, winH);
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
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
            new nint(style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
    }

    private static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const long WsExNoActivate = 0x08000000;
        public const long WsExToolWindow = 0x80;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    }
}

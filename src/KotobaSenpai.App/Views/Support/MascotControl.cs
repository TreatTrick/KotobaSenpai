using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KotobaSenpai.App.Support;

namespace KotobaSenpai.App.Views.Support;

public sealed class MascotControl : Image
{
    public static readonly DependencyProperty IsSupportedProperty = DependencyProperty.Register(
        nameof(IsSupported), typeof(bool), typeof(MascotControl), new PropertyMetadata(false, OnSupportChanged));
    public static readonly DependencyProperty TargetElementProperty = DependencyProperty.Register(
        nameof(TargetElement), typeof(FrameworkElement), typeof(MascotControl));
    public bool IsSupported { get => (bool)GetValue(IsSupportedProperty); set => SetValue(IsSupportedProperty, value); }
    public FrameworkElement? TargetElement { get => (FrameworkElement?)GetValue(TargetElementProperty); set => SetValue(TargetElementProperty, value); }
    public bool IsPopup { get; set; }
    private readonly MascotPlayback _playback = new();
    private readonly ScaleTransform _breath = new();
    private Window? _window;
    private Application? _application;
    private bool _running;
    private bool _decoding;
    private bool _failed;
    private string? _shown;
    private long _lastTick;
    private DpiScale _lastDpi;
    private int _lifetime;
    internal bool IsRendering => _running;
    private static bool ReduceMotion => !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast;
    private string FramePath => ReduceMotion || _failed
        ? (_playback.Phase == MascotPhase.FollowingMouse ? "happy.png" : "thanks.png")
        : $"{_playback.Clip}/{_playback.Frame:000}.png";

    public MascotControl()
    {
        Stretch = Stretch.Uniform;
        RenderTransform = _breath;
        RenderTransformOrigin = new Point(0.5, 1);
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateRendering();
    }

    public void Celebrate() { _playback.Celebrate(); _ = ShowCurrentAsync(); }

    private static void OnSupportChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (MascotControl)sender;
        if ((bool)e.NewValue)
        {
            if (control.IsLoaded) control._playback.Celebrate();
            else control._playback.Reset(true);
        }
        else control._playback.Reset(false);
        _ = control.ShowCurrentAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (IsPopup && _playback.Phase == MascotPhase.FollowingMouse) _playback.Direction = 1;
        _window = Window.GetWindow(this);
        _application = Application.Current;
        if (_application is not null)
        {
            _application.Activated += OnWindowChanged;
            _application.Deactivated += OnWindowChanged;
        }
        if (_window is not null)
        {
            _window.Activated += OnWindowChanged;
            _window.Deactivated += OnWindowChanged;
            _window.StateChanged += OnWindowChanged;
        }
        SystemParameters.StaticPropertyChanged += OnSystemChanged;
        UpdateRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime++;
        StopRendering();
        if (_window is not null)
        {
            _window.Activated -= OnWindowChanged;
            _window.Deactivated -= OnWindowChanged;
            _window.StateChanged -= OnWindowChanged;
        }
        SystemParameters.StaticPropertyChanged -= OnSystemChanged;
        if (_application is not null)
        {
            _application.Activated -= OnWindowChanged;
            _application.Deactivated -= OnWindowChanged;
        }
        _application = null;
        _window = null;
    }

    private void OnWindowChanged(object? sender, EventArgs e)
    {
        UpdateRendering();
        // Activation events can arrive before the next owned window's IsActive flag is updated.
        _ = Dispatcher.BeginInvoke(UpdateRendering);
    }
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e) => UpdateRendering();

    private void UpdateRendering()
    {
        if (ReduceMotion) _breath.ScaleY = 1;
        var visible = IsLoaded && IsVisible && _window is { WindowState: not WindowState.Minimized };
        var applicationActive = _application?.Windows.OfType<Window>().Any(window => window.IsActive) == true;
        if (visible && (IsPopup || applicationActive) && !ReduceMotion && !_failed)
        {
            if (!_running)
            {
                _lastTick = Stopwatch.GetTimestamp();
                _playback.ResetMouseAnchor();
                CompositionTarget.Rendering += OnRendering;
                _running = true;
            }
        }
        else StopRendering();
        if (visible) _ = ShowCurrentAsync();
    }

    private void StopRendering()
    {
        if (!_running) return;
        CompositionTarget.Rendering -= OnRendering;
        _running = false;
        _playback.ResetMouseAnchor();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        if (_playback.Phase == MascotPhase.FollowingMouse && TargetElement is { IsVisible: true } target && _window is not null)
        {
            var cursor = SupportWindows.GetCursorPosition();
            var dpi = VisualTreeHelper.GetDpi(_window);
            if (!_lastDpi.Equals(dpi))
            {
                _playback.ResetMouseAnchor();
                _lastDpi = dpi;
            }
            var point = new Point(cursor.X / dpi.DpiScaleX, cursor.Y / dpi.DpiScaleY);
            var topLeft = target.PointToScreen(new Point());
            var rect = new Rect(topLeft.X / dpi.DpiScaleX, topLeft.Y / dpi.DpiScaleY, target.ActualWidth, target.ActualHeight);
            if (IsPopup)
            {
                var origin = _window.PointToScreen(new Point());
                _playback.FollowPopup(point, rect,
                    new Rect(origin.X / dpi.DpiScaleX, origin.Y / dpi.DpiScaleY, _window.ActualWidth, _window.ActualHeight));
            }
            else
                _playback.Follow(point, rect, true);
        }
        _playback.Advance(Stopwatch.GetElapsedTime(_lastTick, now).TotalSeconds);
        _lastTick = now;
        _breath.ScaleY = _playback.Breath;
        _ = ShowCurrentAsync();
    }

    private async Task ShowCurrentAsync()
    {
        if (!IsLoaded || !IsVisible || _decoding) return;
        var path = FramePath;
        if (_shown == path) return;
        _decoding = true;
        var lifetime = _lifetime;
        try
        {
            var image = await MascotFrames.Shared.GetAsync(path);
            if (lifetime == _lifetime && IsLoaded && IsVisible)
            {
                Source = image;
                _shown = path;
                if (ReduceMotion) _breath.ScaleY = 1;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("Failed to decode support mascot: {0}", ex);
            _failed = true;
            StopRendering();
        }
        finally { _decoding = false; }
        if ((_failed && path is not ("happy.png" or "thanks.png")) || (!_running && !_failed && _shown != FramePath))
            await ShowCurrentAsync();
    }
}

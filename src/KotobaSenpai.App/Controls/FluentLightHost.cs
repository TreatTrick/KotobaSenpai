using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KotobaSenpai.App.Controls;

/// <summary>Shares one pointer and an idle-aware rendering loop across a page or popup.</summary>
public sealed class FluentLightHost : AdornerDecorator
{
    public static readonly DependencyProperty ReactToPointerWhenInactiveProperty = DependencyProperty.Register(
        nameof(ReactToPointerWhenInactive), typeof(bool), typeof(FluentLightHost), new PropertyMetadata(false,
            (sender, _) => ((FluentLightHost)sender).RefreshActivity()));

    private static readonly DependencyProperty ThemeBrushProperty = DependencyProperty.Register(
        "ThemeBrush", typeof(Brush), typeof(FluentLightHost), new PropertyMetadata(Brushes.White,
            (sender, _) => ((FluentLightHost)sender).Wake()));

    private readonly FluentLightMotion _motion = new();
    private readonly Dictionary<FrameworkElement, FluentLightAdorner.Surface> _surfaces = [];
    private FluentLightAdorner? _adorner;
    private Window? _window;
    private FrameworkElement? _hovered;
    private Point? _pointer;
    private TimeSpan? _lastFrame;
    private bool _geometryDirty = true;

    internal bool IsRendering { get; private set; }

    /// <summary>Allows non-activating interactive overlays to illuminate while the pointer is present or fading out.</summary>
    public bool ReactToPointerWhenInactive
    {
        get => (bool)GetValue(ReactToPointerWhenInactiveProperty);
        set => SetValue(ReactToPointerWhenInactiveProperty, value);
    }

    private bool CanRenderInWindow => _window is not { WindowState: WindowState.Minimized }
        && (_window is not { IsActive: false } || ReactToPointerWhenInactive
            && (_pointer is not null || _motion.Presence > 0 || _surfaces.Values.Any(surface => surface.Hover > 0)));

    public FluentLightHost()
    {
        SetResourceReference(ThemeBrushProperty, "TextFillColorPrimaryBrush");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnVisibilityChanged;
    }

    internal void Register(FrameworkElement element)
    {
        if (!_surfaces.TryAdd(element, new FluentLightAdorner.Surface(element))) return;
        element.IsEnabledChanged += OnSurfaceChanged;
        element.IsVisibleChanged += OnSurfaceChanged;
        _geometryDirty = true;
        Wake();
    }

    internal void Unregister(FrameworkElement element)
    {
        if (!_surfaces.Remove(element)) return;
        element.IsEnabledChanged -= OnSurfaceChanged;
        element.IsVisibleChanged -= OnSurfaceChanged;
        if (ReferenceEquals(_hovered, element)) _hovered = null;
        _adorner?.InvalidateVisual();
        Wake();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_adorner is not null || Child is null) return;
        _adorner = new FluentLightAdorner(Child, _surfaces.Values);
        AdornerLayer.Add(_adorner);
        _window = Window.GetWindow(this);
        if (_window is not null)
        {
            _window.Deactivated += OnDeactivated;
            _window.Activated += OnActivated;
            _window.StateChanged += OnWindowStateChanged;
        }
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        LayoutUpdated += OnLayoutUpdated;
        _geometryDirty = true;
        if (IsMouseOver) UpdatePointer(Mouse.GetPosition(this));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Reset();
        LayoutUpdated -= OnLayoutUpdated;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        if (_window is not null)
        {
            _window.Deactivated -= OnDeactivated;
            _window.Activated -= OnActivated;
            _window.StateChanged -= OnWindowStateChanged;
            _window = null;
        }
        if (_adorner is not null) AdornerLayer.Remove(_adorner);
        _adorner = null;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        UpdatePointer(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdatePointer(null);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Wake();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Wake();
    }

    internal void UpdatePointer(Point? position)
    {
        _pointer = position;
        _motion.SetPointer(position);
        _hovered = null;
        if (position is { } point)
        {
            for (var hit = InputHitTest(point) as DependencyObject; hit is not null && !ReferenceEquals(hit, this);
                 hit = hit is Visual ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit))
            {
                if (hit is not FrameworkElement element || FluentLight.GetKind(element) != FluentLightKind.Button) continue;
                if (element.IsEnabled) _hovered = element;
                break;
            }
        }
        Wake();
    }

    private void Wake()
    {
        if (!IsLoaded || !IsVisible || _adorner is null || SystemParameters.HighContrast) return;
        if (!CanRenderInWindow) return;
        if (IsRendering) return;
        IsRendering = true;
        _lastFrame = null;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Stop()
    {
        if (!IsRendering) return;
        CompositionTarget.Rendering -= OnRendering;
        IsRendering = false;
        _lastFrame = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!CanRenderInWindow) { Reset(); return; }
        if (e is not RenderingEventArgs rendering) return;
        var seconds = _lastFrame is { } previous ? (rendering.RenderingTime - previous).TotalSeconds : 1d / 60;
        _lastFrame = rendering.RenderingTime;
        Advance(Math.Clamp(seconds, 0, 0.064), SystemParameters.ClientAreaAnimation);
    }

    internal void Advance(double seconds, bool animate)
    {
        if (_adorner is null) return;
        if (SystemParameters.HighContrast) { Reset(); return; }
        _motion.Advance(seconds, animate);
        if (_geometryDirty && Child is not null)
        {
            foreach (var surface in _surfaces.Values) surface.Measure(Child);
            _geometryDirty = false;
        }
        var settled = _motion.IsSettled;
        foreach (var surface in _surfaces.Values)
        {
            var hovered = surface.Element.IsEnabled && surface.Element.IsVisible
                && (ReferenceEquals(surface.Element, _hovered) || surface.Element.IsKeyboardFocusWithin);
            var target = surface.Kind == FluentLightKind.Button && hovered ? 1d : 0d;
            surface.Hover = FluentLightMotion.Fade(surface.Hover, target, seconds, animate);
            settled &= surface.Hover == target;
        }
        var foreground = (GetValue(ThemeBrushProperty) as SolidColorBrush)?.Color ?? Colors.White;
        _adorner.Update(_motion.Position, _motion.Presence, foreground.R + foreground.G + foreground.B > 384);
        if (settled) Stop();
    }

    private void Reset()
    {
        Stop();
        _pointer = null;
        _hovered = null;
        _motion.SetPointer(null);
        _motion.Advance(0, animate: false);
        foreach (var surface in _surfaces.Values) surface.Hover = 0;
        _adorner?.Update(_motion.Position, 0, dark: true);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_motion.Presence == 0 && !_surfaces.Values.Any(surface => surface.Hover > 0))
        {
            _geometryDirty = true;
            return;
        }
        if (Child is null) return;
        var changed = false;
        foreach (var surface in _surfaces.Values) changed |= surface.Measure(Child);
        _geometryDirty = false;
        // LayoutUpdated also follows our own drawing. Wake only when bounds or viewport clips actually change.
        if (changed) UpdatePointer(_pointer);
    }

    private void OnSurfaceChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _geometryDirty = true;
        Wake();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) Reset();
    }

    private void RefreshActivity()
    {
        if (CanRenderInWindow) Wake();
        else Reset();
    }

    private void OnDeactivated(object? sender, EventArgs e) => RefreshActivity();
    private void OnActivated(object? sender, EventArgs e)
    {
        UpdatePointer(IsMouseOver ? Mouse.GetPosition(this) : null);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window?.WindowState == WindowState.Minimized) Reset();
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded) return;
            if (SystemParameters.HighContrast) Reset();
            else Wake();
        });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KotobaSenpai.Platform.Windows.Guidance;
using KotobaSenpai.Core.Overlay;

namespace KotobaSenpai.App.Guidance.Presentation;

public sealed class MainInterfaceTourPresenter : IDisposable
{
    private readonly Grid _root;
    private readonly UIElement _surface;
    private readonly Func<GuidanceTarget, FrameworkElement?> _resolveTarget;
    private readonly ITourPresentation _presentation;
    private readonly GuidanceOverlay _overlay = new((bubble, button) => GuidanceLight.DecorateBubble(bubble, button));
    private readonly bool _surfaceWasEnabled;
    private readonly IInputElement? _previousFocus;
    private GuidanceTarget? _target;
    private Rect _lastBounds = Rect.Empty;
    private Size _lastArea;
    private bool _disposed;

    public MainInterfaceTourPresenter(Grid root, UIElement surface, Func<GuidanceTarget, FrameworkElement?> resolveTarget,
        ITourPresentation presentation)
    {
        _root = root;
        _surface = surface;
        _resolveTarget = resolveTarget;
        _presentation = presentation;
        _surfaceWasEnabled = surface.IsEnabled;
        _previousFocus = Keyboard.FocusedElement;
        presentation.Changed += OnChanged;
        _overlay.Acknowledged += OnAcknowledged;
        root.LayoutUpdated += OnLayoutUpdated;
        presentation.Start();
    }

    private void OnAcknowledged(object? sender, EventArgs e) => _presentation.Advance();
    private void OnLayoutUpdated(object? sender, EventArgs e) => UpdatePlacement();

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!_root.Dispatcher.CheckAccess())
        {
            _root.Dispatcher.Invoke(() => OnChanged(sender, e));
            return;
        }
        if (_disposed) return;
        if (_presentation.Target is not { } target)
        {
            Hide();
            return;
        }
        if (!_root.Children.Contains(_overlay)) _root.Children.Add(_overlay);
        _surface.IsEnabled = false;
        _overlay.SetText(_presentation.Text, _presentation.AcknowledgeText);
        _lastBounds = Rect.Empty;
        if (_target != target)
        {
            _target = target;
            _resolveTarget(target)?.BringIntoView();
            _root.Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || _presentation.Target is null) return;
                UpdatePlacement();
                _overlay.Focus();
            }, DispatcherPriority.Loaded);
        }
        UpdatePlacement();
    }

    private void UpdatePlacement()
    {
        if (_disposed || _presentation.Target is not { } target
            || _resolveTarget(target) is not { IsVisible: true } anchor || !_root.IsAncestorOf(anchor)) return;
        var bounds = anchor.TransformToAncestor(_root).TransformBounds(new Rect(anchor.RenderSize));
        var area = _root.RenderSize;
        if (bounds == _lastBounds && area == _lastArea) return;
        _lastBounds = bounds;
        _lastArea = area;
        _overlay.Place(bounds, area);
    }

    private void Hide()
    {
        _root.Children.Remove(_overlay);
        _surface.IsEnabled = _surfaceWasEnabled;
        _target = null;
        if (_previousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
        else _surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _presentation.Changed -= OnChanged;
        _overlay.Acknowledged -= OnAcknowledged;
        _root.LayoutUpdated -= OnLayoutUpdated;
        Hide();
        _presentation.Dispose();
    }
}

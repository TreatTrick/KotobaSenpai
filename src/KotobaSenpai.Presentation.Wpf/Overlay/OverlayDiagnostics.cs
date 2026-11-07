using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Collects native window and WPF layout diagnostics for the overlay surface.</summary>
internal sealed class OverlayDiagnostics(ILogger? logger)
{
    private readonly ILogger? _logger = logger;
    private int _layoutGeneration;

    public void LogWindowState(Window window, nint ownerHandle, WordOverlaySession? session, string reason)
    {
        if (_logger is null)
            return;
        var handle = new WindowInteropHelper(window).Handle;
        OverlayWindowNativeHost.LogDiagnostics(_logger, handle, ownerHandle, reason,
            session?.Id.ToString() ?? "<none>", window.IsVisible, window.Visibility.ToString(), window.WindowState.ToString());
    }

    public void ScheduleAfterLayout(Window window, Canvas canvas, OverlayAnnotationLayer annotations,
        double fontScale, nint ownerHandle, WordOverlaySession? session)
    {
        if (_logger is null)
            return;

        var generation = ++_layoutGeneration;
        window.Dispatcher.BeginInvoke(() =>
        {
            if (generation != _layoutGeneration)
                return;
            LogWindowState(window, ownerHandle, session, "after-layout");
            LogCanvasLayout(window, canvas, annotations.UnderlineCount, fontScale, session);
        }, DispatcherPriority.ContextIdle);
    }

    public void InvalidateScheduledLayout() => _layoutGeneration++;

    private void LogCanvasLayout(Window window, Canvas canvas, int underlineCount, double fontScale, WordOverlaySession? session)
    {
        var canvasBounds = new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight);
        var contentBounds = Rect.Empty;
        var outside = 0;
        var clipped = 0;
        var unmeasured = 0;
        foreach (var child in canvas.Children.OfType<FrameworkElement>())
        {
            if (child.RenderSize.Width <= 0 || child.RenderSize.Height <= 0)
            {
                unmeasured++;
                continue;
            }
            var left = Canvas.GetLeft(child);
            var top = Canvas.GetTop(child);
            var childBounds = new Rect(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top,
                child.RenderSize.Width, child.RenderSize.Height);
            contentBounds.Union(childBounds);
            var intersection = Rect.Intersect(canvasBounds, childBounds);
            if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0)
                outside++;
            else if (!canvasBounds.Contains(childBounds))
                clipped++;
        }
        var dpi = VisualTreeHelper.GetDpi(window);
        _logger!.Log(LogLevel.Trace, null, "[OVERLAY] Canvas layout: sessionId={0}, widthDip={1:0.###}, heightDip={2:0.###}, actualWidthDip={3:0.###}, actualHeightDip={4:0.###}, dpiScaleX={5:0.###}, dpiScaleY={6:0.###}, opacity={7:0.###}, canvasOpacity={8:0.###}, canvasVisible={9}, canvasBoundsDip={10}, contentBoundsDip={11}, childCount={12}, outsideChildren={13}, clippedChildren={14}, unmeasuredChildren={15}, underlineCount={16}, fontScale={17:0.###}.",
            session?.Id.ToString() ?? "<none>", window.Width, window.Height, window.ActualWidth, window.ActualHeight,
            dpi.DpiScaleX, dpi.DpiScaleY, window.Opacity, canvas.Opacity, canvas.IsVisible, canvasBounds, contentBounds,
            canvas.Children.Count, outside, clipped, unmeasured, underlineCount, fontScale);
    }
}

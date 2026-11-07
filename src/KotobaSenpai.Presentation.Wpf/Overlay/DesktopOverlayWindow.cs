using System.Windows;
using System.Windows.Interop;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>A non-activating, native click-through desktop presentation surface.</summary>
public class DesktopOverlayWindow : Window
{
    public DesktopOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Topmost = true;
    }

    public static ScreenRect DesktopBounds => DesktopOverlayNativeHost.DesktopBounds;

    public void ShowOverDesktop()
    {
        // ponytail: one pixel-mapped desktop surface avoids monitor seams; split it only for desktops exceeding render texture limits.
        Show();
        PositionOverDesktop();
    }

    /// <summary>Converts physical screen pixels using this surface's actual transform, including negative origins.</summary>
    public Rect FromScreenPixels(Rect pixels)
    {
        var topLeft = PointFromScreen(pixels.TopLeft);
        var bottomRight = PointFromScreen(pixels.BottomRight);
        return new Rect(topLeft, bottomRight);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        OverlayWindowNativeHost.ConfigureInput(handle, clickThrough: true);
        OverlayWindowRegistry.Register(handle);
        Closed += (_, _) => OverlayWindowRegistry.Unregister(handle);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionOverDesktop);
    }

    private void PositionOverDesktop()
    {
        if (!IsVisible) return;
        DesktopOverlayNativeHost.PositionOverDesktop(new WindowInteropHelper(this).Handle);
    }
}

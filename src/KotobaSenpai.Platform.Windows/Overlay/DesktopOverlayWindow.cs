using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Platform.Windows.Overlay;

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

    public static ScreenRect DesktopBounds => new(
        Native.GetSystemMetrics(76), Native.GetSystemMetrics(77),
        Native.GetSystemMetrics(78), Native.GetSystemMetrics(79));

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
        var style = Native.GetWindowLongPtr(handle, -20).ToInt64();
        Native.SetWindowLongPtr(handle, -20, (nint)(style | 0x08000000 | 0x20 | 0x80));
        OverlayWindowRegistry.Register(handle);
        Closed += (_, _) => OverlayWindowRegistry.Unregister(handle);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionOverDesktop);
    }

    private void PositionOverDesktop()
    {
        if (!IsVisible) return;
        var bounds = DesktopBounds;
        if (!Native.SetWindowPos(new WindowInteropHelper(this).Handle, (nint)(-1),
                bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to position the desktop presentation surface.");
    }

    private static class Native
    {
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    }
}

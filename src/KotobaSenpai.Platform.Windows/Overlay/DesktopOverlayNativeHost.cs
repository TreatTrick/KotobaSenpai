using System.ComponentModel;
using System.Runtime.InteropServices;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Positions click-through HWNDs over the physical virtual desktop.</summary>
internal static class DesktopOverlayNativeHost
{
    public static ScreenRect DesktopBounds => new(
        Native.GetSystemMetrics(76), Native.GetSystemMetrics(77),
        Native.GetSystemMetrics(78), Native.GetSystemMetrics(79));

    public static void PositionOverDesktop(nint handle)
    {
        var bounds = DesktopBounds;
        if (!WindowNativeMethods.SetWindowPos(handle, (nint)(-1), bounds.X, bounds.Y,
                bounds.Width, bounds.Height, WindowNativeMethods.SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to position the desktop presentation surface.");
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
    }
}

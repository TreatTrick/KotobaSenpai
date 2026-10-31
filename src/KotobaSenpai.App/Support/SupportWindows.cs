using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KotobaSenpai.App.Support;

internal static class SupportWindows
{
    public static Point GetCursorPosition()
    {
        GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }

    public static bool ForegroundIsFullScreen()
    {
        var handle = GetForegroundWindow();
        if (handle == 0 || !GetWindowRect(handle, out var rect)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return false;
        var screen = info.Monitor;
        return rect.Left <= screen.Left + 2 && rect.Top <= screen.Top + 2
            && rect.Right >= screen.Right - 2 && rect.Bottom >= screen.Bottom - 2;
    }

    public static void Position(Window window, Window owner, bool centerOnOwner)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(owner).Handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var dpi = GetDpiForWindow(new WindowInteropHelper(owner).Handle) / 96.0;
        var width = (int)Math.Round(window.ActualWidth * dpi);
        var height = (int)Math.Round(window.ActualHeight * dpi);
        var margin = (int)Math.Round(20 * dpi);
        var x = info.Work.Right - width - margin;
        var y = info.Work.Bottom - height - margin;
        if (centerOnOwner && GetWindowRect(new WindowInteropHelper(owner).Handle, out var ownerRect))
        {
            x = ownerRect.Left + (ownerRect.Right - ownerRect.Left - width) / 2;
            y = ownerRect.Top + (ownerRect.Bottom - ownerRect.Top - height) / 2;
            x = Math.Clamp(x, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
            y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        }
        SetWindowPos(new WindowInteropHelper(window).Handle, 0,
            x, y, width, height, 0x0014);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
}

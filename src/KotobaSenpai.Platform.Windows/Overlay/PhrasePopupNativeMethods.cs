using System.Runtime.InteropServices;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Native popup placement and low-level input imports; all payloads use physical pixels.</summary>
internal static class PhrasePopupNativeMethods
{
    public const int WmMouseActivate = 0x0021;
    public const int WmMouseWheel = 0x020A;
    public const int MaNoActivate = 3;
    public const uint MonitorDefaultToNearest = 2;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoZOrder = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly ScreenRect ToScreenRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public int Size;
        public Rect Bounds, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    public static extern nint MonitorFromRect(in Rect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint handle, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

    public const int WmLeftButtonDown = 0x0201;
    public const int WmLeftButtonUp = 0x0202;
    public delegate nint MouseHook(int code, nint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseHookData
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int kind, MouseHook callback, nint module, uint thread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point point);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static extern nint GetModuleHandle(string? name);
}

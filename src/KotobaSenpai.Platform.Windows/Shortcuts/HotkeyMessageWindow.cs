using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KotobaSenpai.Platform.Windows.Shortcuts;

/// <summary>Owns a message-only HWND and its class registration on one native thread.</summary>
internal sealed class HotkeyMessageWindow : IDisposable
{
    private readonly Native.WindowProcedure _procedure;
    private readonly Action<int> _onHotkey;
    private readonly string _className = "KotobaSenpai.Shortcuts." + Guid.NewGuid().ToString("N");
    private readonly nint _instance;
    private bool _disposed;

    public HotkeyMessageWindow(Action<int> onHotkey)
    {
        _onHotkey = onHotkey;
        _procedure = WindowProc;
        _instance = Native.GetModuleHandle(null);
        var windowClass = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_procedure),
            Instance = _instance,
            ClassName = _className,
        };
        if (Native.RegisterClassEx(ref windowClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register the shortcut message window class.");
        Handle = Native.CreateWindowEx(0, _className, "KotobaSenpai shortcuts", 0, 0, 0, 0, 0,
            new nint(-3), nint.Zero, _instance, nint.Zero);
        if (Handle == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Native.UnregisterClass(_className, _instance);
            throw new Win32Exception(error, "Failed to create the shortcut message window.");
        }
    }

    public nint Handle { get; }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == 0x0312)
        {
            // A native callback must never unwind through user32, including dispatcher shutdown races.
            try { if (!_disposed) _onHotkey(wParam.ToInt32()); }
            catch (Exception) { }
            return nint.Zero;
        }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Native.DestroyWindow(Handle);
        Native.UnregisterClass(_className, _instance);
        GC.KeepAlive(_procedure);
    }

    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WindowClass
        {
            public uint Size;
            public uint Style;
            public nint Procedure;
            public int ClassExtra;
            public int WindowExtra;
            public nint Instance;
            public nint Icon;
            public nint Cursor;
            public nint Background;
            public string? MenuName;
            public string ClassName;
            public nint SmallIcon;
        }

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandle(string? name);
        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WindowClass windowClass);
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
            int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        public static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(nint window);
        [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterClass(string className, nint instance);
    }
}

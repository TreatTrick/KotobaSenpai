using System.Runtime.InteropServices;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>Small native bridges not supplied by the Windows SDK projections.</summary>
internal static class WgcInterop
{
    internal readonly record struct WindowLayout(nint Style, nint ExtendedStyle, uint Dpi);
    internal readonly record struct Geometry(ScreenRect FrameBounds, ScreenRect ClientBounds, uint ProcessId, uint ThreadId, WindowLayout Layout);

    internal static Geometry ReadGeometry(nint hwnd)
    {
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)
            || !GetClientRect(hwnd, out var client) || client.Right <= 0 || client.Bottom <= 0
            || !GetWindowRect(hwnd, out var bounds))
            throw new WindowsPlatformException(ErrorCodes.TargetWindowUnavailable, "The target window must exist, be visible, and be restored.");
        var origin = new Point();
        if (!ClientToScreen(hwnd, ref origin))
            throw new WindowsPlatformException(ErrorCodes.TargetWindowUnavailable, "The target client origin is unavailable.");
        if (DwmGetWindowAttribute(hwnd, 9, out var visible, Marshal.SizeOf<Rect>()) >= 0)
            bounds = visible;
        var threadId = GetWindowThreadProcessId(hwnd, out var processId);
        return new Geometry(bounds.ToScreenRect(), new ScreenRect(origin.X, origin.Y, client.Right, client.Bottom), processId, threadId,
            new WindowLayout(GetWindowStyle(hwnd, -16), GetWindowStyle(hwnd, -20), GetDpiForWindow(hwnd)));
    }

    internal static ScreenRect? MatchFrameBounds(nint hwnd, Geometry geometry, int width, int height)
    {
        if (geometry.FrameBounds.Width == width && geometry.FrameBounds.Height == height)
            return geometry.FrameBounds;
        if (GetWindowRect(hwnd, out var window) && window.Right - window.Left == width && window.Bottom - window.Top == height)
            return window.ToScreenRect();
        return null;
    }

    internal static GraphicsCaptureItem CreateItem(nint hwnd)
    {
        const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var hstring));
        try
        {
            var iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, in iid, out var factory));
            try
            {
                var method = Marshal.GetDelegateForFunctionPointer<CreateForWindow>(Method(factory));
                var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                Marshal.ThrowExceptionForHR(method(factory, hwnd, in itemIid, out var pointer));
                try { return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(pointer); }
                finally { Marshal.Release(pointer); }
            }
            finally { Marshal.Release(factory); }
        }
        finally { WindowsDeleteString(hstring); }
    }

    internal static IDirect3DDevice CreateWinrtDevice(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var pointer));
        try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    internal static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var surfacePointer = WinRT.MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var accessIid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfacePointer, in accessIid, out var access));
            try
            {
                var method = Marshal.GetDelegateForFunctionPointer<GetInterface>(Method(access));
                var textureIid = typeof(ID3D11Texture2D).GUID;
                Marshal.ThrowExceptionForHR(method(access, in textureIid, out var texture));
                return new ID3D11Texture2D(texture);
            }
            finally { Marshal.Release(access); }
        }
        finally { Marshal.Release(surfacePointer); }
    }

    // Both interop interfaces expose their first method directly after IUnknown's three slots.
    private static nint Method(nint instance) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), 3 * IntPtr.Size);
    private static nint GetWindowStyle(nint hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtrW(hwnd, index) : GetWindowLongW(hwnd, index);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindow(nint factory, nint hwnd, in Guid iid, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInterface(nint access, in Guid iid, out nint result);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly ScreenRect ToScreenRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint result);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int size);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLongW(nint hwnd, int index);
}

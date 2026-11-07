using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Owns native input styles, DPI and target-relative placement shared by overlay surfaces.</summary>
internal static class OverlayWindowNativeHost
{
    public static double ReadDpiScale(nint handle)
    {
        var dpi = WindowNativeMethods.GetDpiForWindow(handle);
        return (dpi == 0 ? 96 : dpi) / 96d;
    }

    public static void ConfigureInput(nint handle, bool clickThrough)
    {
        if (handle == nint.Zero)
            return;
        var style = WindowNativeMethods.GetWindowLongPtr(handle, WindowNativeMethods.GwlExStyle).ToInt64();
        style = clickThrough
            ? style | WindowNativeMethods.WsExTransparent
            : style & ~WindowNativeMethods.WsExTransparent;
        WindowNativeMethods.SetWindowLongPtr(handle, WindowNativeMethods.GwlExStyle,
            new nint(style | WindowNativeMethods.WsExNoActivate | WindowNativeMethods.WsExToolWindow));
    }

    public static void PositionAboveTarget(nint handle, WindowTarget target, int edgePaddingPixels = 0, ILogger? logger = null)
    {
        var insertAfter = WindowNativeMethods.GetWindow(target.Handle, WindowNativeMethods.GwHwndPrev);
        var flags = WindowNativeMethods.SwpNoActivate | WindowNativeMethods.SwpShowWindow;
        if (insertAfter == handle)
        {
            insertAfter = nint.Zero;
            flags |= WindowNativeMethods.SwpNoZOrder;
        }
        if (!WindowNativeMethods.SetWindowPos(handle, insertAfter,
            target.Bounds.X - edgePaddingPixels, target.Bounds.Y - edgePaddingPixels, 0, 0,
            flags | WindowNativeMethods.SwpNoSize))
            logger?.LogWarning("[OVERLAY] SetWindowPos failed: hwnd={0}, targetHandle={1}, insertAfter={2}, error={3}.",
                handle, target.Handle, insertAfter, System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    public static bool TryGetCursorPosition(out int x, out int y)
    {
        var found = WindowNativeMethods.GetCursorPos(out var point);
        x = point.X;
        y = point.Y;
        return found;
    }

    public static void LogDiagnostics(ILogger logger, nint handle, nint ownerHandle, string reason,
        string sessionId, bool isVisible, string visibility, string windowState)
    {
        var hasBounds = WindowNativeMethods.GetWindowRect(handle, out var bounds);
        var hasClient = WindowNativeMethods.GetClientRect(handle, out var client);
        logger.Log(LogLevel.Trace, null, "[OVERLAY] Window state: reason={0}, sessionId={1}, hwnd={2}, targetHandle={3}, wpfVisible={4}, visibility={5}, windowState={6}, nativeVisible={7}, nativeMinimized={8}, nativeBoundsKnown={9}, nativeBounds={10}, nativeClientKnown={11}, nativeClient={12}, dpi={13}.",
            reason, sessionId, handle, ownerHandle, isVisible, visibility, windowState,
            WindowNativeMethods.IsWindowVisible(handle), WindowNativeMethods.IsIconic(handle), hasBounds, bounds,
            hasClient, client, WindowNativeMethods.GetDpiForWindow(handle));
        logger.Log(LogLevel.Trace, null, "[OVERLAY] Z order: reason={0}, hwnd={1}, nativeOwner={2}, expectedOwner={3}, overlayAbove={4}, overlayBelow={5}, targetAbove={6}, targetBelow={7}, foreground={8}, style=0x{9:X}, exStyle=0x{10:X}, targetStyle=0x{11:X}, targetExStyle=0x{12:X}.",
            reason, handle, WindowNativeMethods.GetWindow(handle, WindowNativeMethods.GwOwner), ownerHandle,
            WindowNativeMethods.GetWindow(handle, WindowNativeMethods.GwHwndPrev), WindowNativeMethods.GetWindow(handle, WindowNativeMethods.GwHwndNext),
            WindowNativeMethods.GetWindow(ownerHandle, WindowNativeMethods.GwHwndPrev), WindowNativeMethods.GetWindow(ownerHandle, WindowNativeMethods.GwHwndNext),
            WindowNativeMethods.GetForegroundWindow(), WindowNativeMethods.GetWindowLongPtr(handle, WindowNativeMethods.GwlStyle).ToInt64(),
            WindowNativeMethods.GetWindowLongPtr(handle, WindowNativeMethods.GwlExStyle).ToInt64(),
            WindowNativeMethods.GetWindowLongPtr(ownerHandle, WindowNativeMethods.GwlStyle).ToInt64(),
            WindowNativeMethods.GetWindowLongPtr(ownerHandle, WindowNativeMethods.GwlExStyle).ToInt64());
    }
}

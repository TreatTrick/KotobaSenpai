using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Reads a target snapshot without owning event hooks or publication.</summary>
internal sealed class TargetWindowSnapshotReader(ILogger? logger)
{
    private readonly WindowOcclusionEvaluator _occlusion = new(logger);

    public void Reset() => _occlusion.Reset();

    public TargetWindowSnapshot Read(nint handle, string title, ScreenRect fallbackBounds,
        uint expectedThread, uint expectedProcess, bool destroyed)
    {
        var threadId = WindowNativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (!WindowNativeMethods.IsWindow(handle)
            || !IsSameIdentity(expectedThread, expectedProcess, threadId, processId, destroyed))
            return new TargetWindowSnapshot(handle, title, fallbackBounds, 1, false, true, false, false, false);

        var visible = WindowNativeMethods.IsWindowVisible(handle);
        var minimized = WindowNativeMethods.IsIconic(handle);
        if (!WindowNativeMethods.GetClientRect(handle, out var rect)
            || !WindowNativeMethods.ClientToScreen(handle, out var origin)
            || rect.Right <= rect.Left
            || rect.Bottom <= rect.Top)
            return new TargetWindowSnapshot(handle, title, fallbackBounds, OverlayWindowNativeHost.ReadDpiScale(handle), visible, minimized, false);

        var bounds = new ScreenRect(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return new TargetWindowSnapshot(
            handle,
            title,
            bounds,
            OverlayWindowNativeHost.ReadDpiScale(handle),
            visible,
            minimized,
            WindowNativeMethods.GetForegroundWindow() == handle,
            _occlusion.IsOccluded(bounds, handle));
    }

    internal static bool IsSameIdentity(uint expectedThread, uint expectedProcess, uint thread, uint process, bool destroyed)
        => !destroyed && expectedThread != 0 && expectedProcess != 0
            && thread == expectedThread && process == expectedProcess;
}

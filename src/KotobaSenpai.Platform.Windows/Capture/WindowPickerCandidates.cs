using System.Runtime.InteropServices;
using System.Text;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>Reads native window identities, bounds and visible candidates independently of presentation.</summary>
internal static class WindowPickerCandidates
{
    public static WindowPickerCandidate? ResolveAt(int x, int y, int excludedProcessId)
    {
        var point = new WindowPickerNativeMethods.Point { X = x, Y = y };
        var helpers = OverlayWindowRegistry.Snapshot();
        var hit = WindowPickerNativeMethods.GetAncestor(WindowPickerNativeMethods.WindowFromPoint(point), 2);
        WindowPickerNativeMethods.GetWindowThreadProcessId(hit, out var processId);
        if (hit != 0 && processId != excludedProcessId && !helpers.Contains(hit))
            return ReadCandidate(hit, excludedProcessId);

        WindowPickerCandidate? result = null;
        // Only our own presentation surfaces are skipped; a real external window blocks windows behind it.
        WindowPickerNativeMethods.EnumWindows((handle, _) =>
        {
            WindowPickerNativeMethods.GetWindowThreadProcessId(handle, out var pid);
            if (pid == excludedProcessId || helpers.Contains(handle) || !WindowPickerNativeMethods.IsWindowVisible(handle) || WindowPickerNativeMethods.IsIconic(handle)
                || IsCloaked(handle) || !WindowPickerNativeMethods.GetWindowRect(handle, out var bounds)) return true;
            if (WindowPickerNativeMethods.DwmGetWindowAttribute(handle, 9, out WindowPickerNativeMethods.Rect visible, 16) >= 0) bounds = visible;
            if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom) return true;
            result = ReadCandidate(handle, excludedProcessId);
            return false;
        }, 0);
        return result;
    }

    private static bool IsCloaked(nint handle)
        => WindowPickerNativeMethods.DwmGetWindowAttribute(handle, 14, out uint cloaked, sizeof(uint)) >= 0 && cloaked != 0;

    public static WindowPickerCandidate? ReadCandidate(nint handle, int excludedProcessId)
    {
        var threadId = WindowPickerNativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (handle == 0 || processId == excludedProcessId || threadId == 0
            || OverlayWindowRegistry.Snapshot().Contains(handle)
            || !WindowPickerNativeMethods.IsWindow(handle) || !WindowPickerNativeMethods.IsWindowVisible(handle) || WindowPickerNativeMethods.IsIconic(handle)
            || handle == WindowPickerNativeMethods.GetDesktopWindow() || handle == WindowPickerNativeMethods.GetShellWindow() || IsCloaked(handle)) return null;
        var className = new StringBuilder(256);
        WindowPickerNativeMethods.GetClassName(handle, className, className.Capacity);
        if (className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW") return null;
        if (!WindowPickerNativeMethods.GetClientRect(handle, out var client) || client.Right <= 0 || client.Bottom <= 0
            || !WindowPickerNativeMethods.GetWindowRect(handle, out var frame)) return null;
        var origin = new WindowPickerNativeMethods.Point();
        if (!WindowPickerNativeMethods.ClientToScreen(handle, ref origin)) return null;
        if (WindowPickerNativeMethods.DwmGetWindowAttribute(handle, 9, out WindowPickerNativeMethods.Rect visible, 16) >= 0
            && visible.Right > visible.Left && visible.Bottom > visible.Top) frame = visible;
        if (frame.Right <= frame.Left || frame.Bottom <= frame.Top) return null;
        var title = new StringBuilder(WindowPickerNativeMethods.GetWindowTextLength(handle) + 1);
        WindowPickerNativeMethods.GetWindowText(handle, title, title.Capacity);
        if (WindowPickerNativeMethods.GetWindowThreadProcessId(handle, out var latestProcessId) != threadId || latestProcessId != processId)
            return null;
        return new(new WindowTarget(handle, title.ToString(), new ScreenRect(origin.X, origin.Y, client.Right, client.Bottom)),
            new ScreenRect(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top),
            processId, threadId, WindowPickerNativeMethods.GetDpiForWindow(handle));
    }

    public static bool HasSameIdentity(nint handle, uint threadId, uint processId)
        => WindowPickerNativeMethods.GetWindowThreadProcessId(handle, out var currentProcess) == threadId && currentProcess == processId;

    public static ScreenRect GetWorkAreaAt(int x, int y, ScreenRect fallback)
    {
        var monitor = new WindowPickerNativeMethods.MonitorInfo { Size = Marshal.SizeOf<WindowPickerNativeMethods.MonitorInfo>() };
        var point = new WindowPickerNativeMethods.Point { X = x, Y = y };
        return WindowPickerNativeMethods.GetMonitorInfo(WindowPickerNativeMethods.MonitorFromPoint(point, 2), ref monitor)
            ? new ScreenRect(monitor.Work.Left, monitor.Work.Top, monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top)
            : fallback;
    }
}

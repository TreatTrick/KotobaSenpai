using System.Runtime.InteropServices;
using System.Text;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>
/// Enumerates the currently visible, non-minimized, titled top-level windows via Win32 for the user to pick a target.
/// The selection only keeps a handle in-process; no screenshots or raw window content are written.
/// </summary>
public sealed class Win32WindowCatalog : IWindowCatalog
{
    public IReadOnlyList<WindowTarget> ListVisibleWindows()
    {
        var result = new List<WindowTarget>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || IsIconic(handle))
                return true;

            var titleBuilder = new StringBuilder(GetWindowTextLength(handle) + 1);
            _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title) || !GetClientRect(handle, out var rect))
                return true;

            // Use the client area (excluding the title bar/borders) as the capture and coordinate basis: avoids title-bar text
            // interfering with OCR, and matches DokiDokiDict's GetClientRect capture. ClientToScreen converts the client-area
            // origin to screen coordinates.
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0 || !ClientToScreen(handle, out var origin))
                return true;
            result.Add(new WindowTarget(handle, title, new ScreenRect(origin.X, origin.Y, width, height)));
            return true;
        }, nint.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);

    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, out POINT point);
}

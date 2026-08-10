using System.Runtime.InteropServices;
using System.Text;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Capture;

/// <summary>
/// 通过 Win32 枚举当前可见、未最小化且有标题的顶层窗口，供用户选择目标。
/// 选择结果仅在进程内保留句柄，不写入截图或原始窗口内容。
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

            // 用客户区（不含标题栏/边框）作为捕获与坐标基准：避免标题栏文字干扰 OCR，
            // 且与 DokiDokiDict 的 GetClientRect 捕获一致。ClientToScreen 把客户区原点转到屏幕坐标。
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

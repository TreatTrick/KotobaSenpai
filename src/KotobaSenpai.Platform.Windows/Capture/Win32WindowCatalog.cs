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
            if (string.IsNullOrWhiteSpace(title) || !GetWindowRect(handle, out var rect))
                return true;

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width > 0 && height > 0)
                result.Add(new WindowTarget(handle, title, new ScreenRect(rect.Left, rect.Top, width, height)));
            return true;
        }, nint.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private readonly record struct Rect(int Left, int Top, int Right, int Bottom);

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
    private static extern bool GetWindowRect(nint hWnd, out Rect rect);
}

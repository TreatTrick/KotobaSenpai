using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 将 OCR 词框按捕获帧与窗口屏幕矩形的比例映射为屏幕物理像素坐标，并裁剪到窗口边界内。
/// 仅依赖物理像素，与 DPI 无关；DPI 到 DIP 的转换由平台适配器负责。
/// </summary>
public static class CoordinateMapper
{
    public static ScreenRect ToScreen(OcrWord word, int frameWidth, int frameHeight, ScreenRect windowBounds)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));

        var frame = word.FrameBounds;
        static int Scale(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        var left = windowBounds.X + Scale(frame.X * (double)windowBounds.Width / frameWidth);
        var top = windowBounds.Y + Scale(frame.Y * (double)windowBounds.Height / frameHeight);
        var right = windowBounds.X + Scale(frame.Right * (double)windowBounds.Width / frameWidth);
        var bottom = windowBounds.Y + Scale(frame.Bottom * (double)windowBounds.Height / frameHeight);

        var clipped = new ScreenRect(
            Math.Max(windowBounds.X, left),
            Math.Max(windowBounds.Y, top),
            Math.Max(1, Math.Min(windowBounds.Right, right) - Math.Max(windowBounds.X, left)),
            Math.Max(1, Math.Min(windowBounds.Bottom, bottom) - Math.Max(windowBounds.Y, top)));
        return clipped.ClampTo(windowBounds);
    }
}

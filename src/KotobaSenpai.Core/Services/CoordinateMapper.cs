using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Maps OCR word boxes to screen physical-pixel coordinates by the ratio of the capture frame to the window's
/// screen rectangle, clipping into the window bounds. Depends only on physical pixels, independent of DPI;
/// the DPI-to-DIP conversion is handled by the platform adapter.
/// </summary>
public static class CoordinateMapper
{
    public static ScreenRect ToScreen(OcrWord word, int frameWidth, int frameHeight, ScreenRect windowBounds)
        => ToScreen(word.FrameBounds, frameWidth, frameHeight, windowBounds);

    public static ScreenRect ToScreen(ScreenRect frameRect, int frameWidth, int frameHeight, ScreenRect windowBounds)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));

        var frame = frameRect;
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

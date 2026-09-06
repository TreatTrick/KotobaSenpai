using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Platform.Windows.Capture;

internal static class FrameDifference
{
    public static bool Exceeds(CapturedFrame? previous, CapturedFrame current, double threshold)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous is null)
            return false;
        if (previous.Width != current.Width || previous.Height != current.Height)
            return true;

        threshold = Math.Clamp(threshold, 0, 1);
        var previousPixels = previous.Bgra32.Span;
        var currentPixels = current.Bgra32.Span;
        var changedPixels = 0;
        var pixelCount = checked(current.Width * current.Height);
        for (var pixel = 0; pixel < pixelCount; pixel++)
        {
            var offset = pixel * 4;
            if (previousPixels[offset] != currentPixels[offset]
                || previousPixels[offset + 1] != currentPixels[offset + 1]
                || previousPixels[offset + 2] != currentPixels[offset + 2])
                changedPixels++;
        }

        return changedPixels > 0 && (double)changedPixels / pixelCount >= threshold;
    }
}

using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using System.Runtime.InteropServices;

namespace KotobaSenpai.Platform.Windows.Capture;

internal static class WgcFrameLayout
{
    internal static ScreenRect? ResolveCrop(ScreenRect frameBounds, ScreenRect clientBounds, int width, int height, ScreenRect? region)
    {
        if (frameBounds.Width != width || frameBounds.Height != height
            || clientBounds.X < frameBounds.X || clientBounds.Y < frameBounds.Y
            || clientBounds.Right > frameBounds.Right || clientBounds.Bottom > frameBounds.Bottom)
            return null;

        var crop = region ?? new ScreenRect(0, 0, clientBounds.Width, clientBounds.Height);
        if (crop.X < 0 || crop.Y < 0 || crop.Right > clientBounds.Width || crop.Bottom > clientBounds.Height)
            throw new ArgumentOutOfRangeException(nameof(region), "The capture region must be inside the client area.");
        return new ScreenRect(clientBounds.X - frameBounds.X + crop.X, clientBounds.Y - frameBounds.Y + crop.Y, crop.Width, crop.Height);
    }

    internal static CapturedFrame CopyRows(nint source, int rowPitch, int width, int height, long generation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var rowBytes = checked(width * 4);
        if (rowPitch < rowBytes) throw new ArgumentOutOfRangeException(nameof(rowPitch));
        if (source == 0) throw new ArgumentException("The pixel address must not be null.", nameof(source));
        var pixels = new byte[checked(rowBytes * height)];
        for (var row = 0; row < height; row++)
            Marshal.Copy(source + checked(rowPitch * row), pixels, row * rowBytes, rowBytes);
        return new CapturedFrame(width, height, pixels, generation);
    }
}

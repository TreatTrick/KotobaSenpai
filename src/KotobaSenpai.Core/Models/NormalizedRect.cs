namespace KotobaSenpai.Core.Models;

/// <summary>A normalized rectangle relative to a target client area.</summary>
public readonly record struct NormalizedRect
{
    public NormalizedRect(double x, double y, double width, double height)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height)
            || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(width) || double.IsInfinity(height)
            || x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Normalized rectangle must be finite and inside the target bounds.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public static NormalizedRect FromScreen(ScreenRect rect, ScreenRect bounds)
    {
        var left = Math.Clamp((rect.X - bounds.X) / (double)bounds.Width, 0, 1);
        var top = Math.Clamp((rect.Y - bounds.Y) / (double)bounds.Height, 0, 1);
        var right = Math.Clamp((rect.Right - bounds.X) / (double)bounds.Width, 0, 1);
        var bottom = Math.Clamp((rect.Bottom - bounds.Y) / (double)bounds.Height, 0, 1);
        if (right <= left)
        {
            left = Math.Max(0, Math.Min(1 - 1d / bounds.Width, left));
            right = Math.Min(1, left + 1d / bounds.Width);
        }
        if (bottom <= top)
        {
            top = Math.Max(0, Math.Min(1 - 1d / bounds.Height, top));
            bottom = Math.Min(1, top + 1d / bounds.Height);
        }
        return new NormalizedRect(left, top, right - left, bottom - top);
    }

    public ScreenRect ToScreen(ScreenRect bounds)
    {
        static int Scale(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        var left = bounds.X + Scale(X * bounds.Width);
        var top = bounds.Y + Scale(Y * bounds.Height);
        var right = bounds.X + Scale(Right * bounds.Width);
        var bottom = bounds.Y + Scale(Bottom * bounds.Height);
        var clippedLeft = Math.Clamp(left, bounds.X, bounds.Right - 1);
        var clippedTop = Math.Clamp(top, bounds.Y, bounds.Bottom - 1);
        var clippedRight = Math.Clamp(right, clippedLeft + 1, bounds.Right);
        var clippedBottom = Math.Clamp(bottom, clippedTop + 1, bounds.Bottom);
        return new ScreenRect(clippedLeft, clippedTop, clippedRight - clippedLeft, clippedBottom - clippedTop);
    }
}

namespace KotobaSenpai.Core.Models;

/// <summary>A physical-pixel rectangle in virtual screen coordinates.</summary>
public readonly record struct ScreenRect
{
    public ScreenRect(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Rectangle width and height must be greater than zero.");

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);

    /// <summary>Clips this rectangle into <paramref name="bounds"/>; throws when there is no intersection.</summary>
    public ScreenRect ClampTo(ScreenRect bounds)
    {
        var left = Math.Max(X, bounds.X);
        var top = Math.Max(Y, bounds.Y);
        var right = Math.Min(Right, bounds.Right);
        var bottom = Math.Min(Bottom, bounds.Bottom);
        return right <= left || bottom <= top
            ? throw new ArgumentException("Rectangle has no valid intersection with bounds.", nameof(bounds))
            : new ScreenRect(left, top, right - left, bottom - top);
    }
}

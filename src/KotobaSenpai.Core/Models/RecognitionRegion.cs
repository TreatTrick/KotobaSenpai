using System.Globalization;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// A recognition region expressed as a window-relative, normalized (0-1) rectangle. Window-size independent so the same
/// region scales to any window; callers convert to window-relative pixels via <see cref="ToPixelRect"/>. Coordinates are
/// relative to the window top-left (0,0), which equals the captured frame's coordinate space.
/// </summary>
public sealed record RecognitionRegion
{
    /// <summary>Minimum region extent as a fraction of the window (guards against a collapsed/degenerate region).</summary>
    public const double MinFraction = 0.05;

    public const string SettingsKey = "RecognitionRegion";

    public RecognitionRegion(double x, double y, double width, double height)
    {
        if (x < 0 || x > 1 || y < 0 || y > 1 || width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Region must be a normalized (0-1) rectangle inside the window.");
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height)
            || double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(width) || double.IsInfinity(height))
            throw new ArgumentOutOfRangeException(nameof(x), "Region coordinates must be finite.");

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

    /// <summary>The full window.</summary>
    public static RecognitionRegion Full { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// Converts to a window-relative pixel rectangle (origin = window top-left), clamped to the window with a minimum
    /// extent. Only <paramref name="windowWidth"/>/<paramref name="windowHeight"/> are needed; the result is relative to
    /// the window, which is also the captured frame's coordinate space.
    /// </summary>
    public ScreenRect ToPixelRect(int windowWidth, int windowHeight)
    {
        if (windowWidth <= 0 || windowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowWidth));

        int minW = Math.Max(1, (int)Math.Round(windowWidth * MinFraction));
        int minH = Math.Max(1, (int)Math.Round(windowHeight * MinFraction));

        int left = (int)Math.Round(X * windowWidth);
        int top = (int)Math.Round(Y * windowHeight);
        int right = (int)Math.Round(Right * windowWidth);
        int bottom = (int)Math.Round(Bottom * windowHeight);

        left = Math.Clamp(left, 0, windowWidth);
        top = Math.Clamp(top, 0, windowHeight);
        right = Math.Clamp(right, left, windowWidth);
        bottom = Math.Clamp(bottom, top, windowHeight);
        if (right - left < minW) right = Math.Min(windowWidth, left + minW);
        if (bottom - top < minH) bottom = Math.Min(windowHeight, top + minH);

        return new ScreenRect(left, top, right - left, bottom - top);
    }

    /// <summary>Converts a window-relative pixel rectangle back to a normalized region.</summary>
    public static RecognitionRegion FromPixelRect(ScreenRect rect, int windowWidth, int windowHeight)
        => new(
            (double)rect.X / windowWidth,
            (double)rect.Y / windowHeight,
            (double)rect.Width / windowWidth,
            (double)rect.Height / windowHeight);

    /// <summary>Serializes to a "x,y,w,h" string for storage.</summary>
    public string Serialize()
        => string.Join(",", new[] { X, Y, Width, Height }
            .Select(v => v.ToString("0.######", CultureInfo.InvariantCulture)));

    public static bool TryParse(string? text, out RecognitionRegion region)
    {
        region = Full;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split(',');
        if (parts.Length != 4
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
            return false;
        try
        {
            region = new RecognitionRegion(x, y, w, h);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
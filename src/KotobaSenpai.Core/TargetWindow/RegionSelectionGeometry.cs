using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Core.TargetWindow;

[Flags]
public enum RegionDragMode
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Move = 16,
    TopLeft = Top | Left,
    TopRight = Top | Right,
    BottomLeft = Bottom | Left,
    BottomRight = Bottom | Right,
}

/// <summary>Maps visible bracket segments to bounded region edits in physical pixels.</summary>
public static class RegionSelectionGeometry
{
    public const double ArmLength = 26;
    public const double ArmThickness = 5;
    private const double HitPadding = 4;
    private const double JunctionLength = 9;

    /// <summary>Resolves a pointer in device-independent coordinates against a physical-pixel region.</summary>
    public static RegionDragMode HitTest(ScreenRect region, double scale, double x, double y)
    {
        var horizontalLength = Math.Min(ArmLength, region.Width / scale / 2);
        var verticalLength = Math.Min(ArmLength, region.Height / scale / 2);
        var corners = new[]
        {
            (region.X, region.Y, 1, 1, RegionDragMode.Left, RegionDragMode.Top),
            (region.Right, region.Y, -1, 1, RegionDragMode.Right, RegionDragMode.Top),
            (region.X, region.Bottom, 1, -1, RegionDragMode.Left, RegionDragMode.Bottom),
            (region.Right, region.Bottom, -1, -1, RegionDragMode.Right, RegionDragMode.Bottom),
        };
        foreach (var (cornerX, cornerY, dirX, dirY, xEdge, yEdge) in corners)
        {
            var dx = (x - cornerX / scale) * dirX;
            var dy = (y - cornerY / scale) * dirY;
            if (dx < -HitPadding || dy < -HitPadding)
                continue;
            if (dx <= Math.Min(JunctionLength, horizontalLength / 2)
                && dy <= Math.Min(JunctionLength, verticalLength / 2))
                return xEdge | yEdge;
            if (dx <= horizontalLength + HitPadding && dy <= ArmThickness + HitPadding)
                return yEdge;
            if (dy <= verticalLength + HitPadding && dx <= ArmThickness + HitPadding)
                return xEdge;
        }
        return x >= region.X / scale && x <= region.Right / scale
            && y >= region.Y / scale && y <= region.Bottom / scale
            ? RegionDragMode.Move : RegionDragMode.None;
    }

    /// <summary>Applies physical-pixel deltas while preserving minimum size and client bounds.</summary>
    public static ScreenRect Drag(ScreenRect initial, RegionDragMode mode, double deltaX, double deltaY, int windowWidth, int windowHeight)
    {
        if (mode == RegionDragMode.Move)
            return new ScreenRect(
                (int)Math.Round(Math.Clamp(initial.X + deltaX, 0, windowWidth - initial.Width)),
                (int)Math.Round(Math.Clamp(initial.Y + deltaY, 0, windowHeight - initial.Height)),
                initial.Width, initial.Height);

        var minWidth = Math.Max(1, (int)Math.Round(windowWidth * RecognitionRegion.MinFraction));
        var minHeight = Math.Max(1, (int)Math.Round(windowHeight * RecognitionRegion.MinFraction));
        int left = initial.X, top = initial.Y, right = initial.Right, bottom = initial.Bottom;
        if ((mode & RegionDragMode.Left) != 0)
            left = (int)Math.Round(Math.Clamp(initial.X + deltaX, 0, Math.Max(0, right - minWidth)));
        if ((mode & RegionDragMode.Right) != 0)
            right = (int)Math.Round(Math.Clamp(initial.Right + deltaX, Math.Min(left + minWidth, windowWidth), windowWidth));
        if ((mode & RegionDragMode.Top) != 0)
            top = (int)Math.Round(Math.Clamp(initial.Y + deltaY, 0, Math.Max(0, bottom - minHeight)));
        if ((mode & RegionDragMode.Bottom) != 0)
            bottom = (int)Math.Round(Math.Clamp(initial.Bottom + deltaY, Math.Min(top + minHeight, windowHeight), windowHeight));
        return new ScreenRect(left, top, right - left, bottom - top);
    }
}

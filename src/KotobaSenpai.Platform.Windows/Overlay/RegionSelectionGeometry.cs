using System.Windows;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Platform.Windows.Overlay;

[Flags]
internal enum RegionDragMode
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
internal static class RegionSelectionGeometry
{
    public const double ArmLength = 26;
    public const double ArmThickness = 5;
    private const double HitPadding = 4;
    private const double JunctionLength = 9;

    public static RegionDragMode HitTest(ScreenRect region, double scale, Point point)
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
        foreach (var (x, y, dirX, dirY, xEdge, yEdge) in corners)
        {
            var dx = (point.X - x / scale) * dirX;
            var dy = (point.Y - y / scale) * dirY;
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
        return RegionDragMode.Move;
    }

    public static ScreenRect Drag(ScreenRect initial, RegionDragMode mode, Vector delta, int windowWidth, int windowHeight)
    {
        if (mode == RegionDragMode.Move)
            return new ScreenRect(
                (int)Math.Round(Math.Clamp(initial.X + delta.X, 0, windowWidth - initial.Width)),
                (int)Math.Round(Math.Clamp(initial.Y + delta.Y, 0, windowHeight - initial.Height)),
                initial.Width, initial.Height);

        var minWidth = Math.Max(1, (int)Math.Round(windowWidth * RecognitionRegion.MinFraction));
        var minHeight = Math.Max(1, (int)Math.Round(windowHeight * RecognitionRegion.MinFraction));
        int left = initial.X, top = initial.Y, right = initial.Right, bottom = initial.Bottom;
        if ((mode & RegionDragMode.Left) != 0)
            left = (int)Math.Round(Math.Clamp(initial.X + delta.X, 0, Math.Max(0, right - minWidth)));
        if ((mode & RegionDragMode.Right) != 0)
            right = (int)Math.Round(Math.Clamp(initial.Right + delta.X, Math.Min(left + minWidth, windowWidth), windowWidth));
        if ((mode & RegionDragMode.Top) != 0)
            top = (int)Math.Round(Math.Clamp(initial.Y + delta.Y, 0, Math.Max(0, bottom - minHeight)));
        if ((mode & RegionDragMode.Bottom) != 0)
            bottom = (int)Math.Round(Math.Clamp(initial.Bottom + delta.Y, Math.Min(top + minHeight, windowHeight), windowHeight));
        return new ScreenRect(left, top, right - left, bottom - top);
    }
}

using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Core.Tests.Geometry;

public sealed class RegionSelectionGeometryTests
{
    [Theory]
    [InlineData(102, 82, "TopLeft")]
    [InlineData(120, 82, "Top")]
    [InlineData(102, 100, "Left")]
    [InlineData(498, 82, "TopRight")]
    [InlineData(480, 82, "Top")]
    [InlineData(498, 100, "Right")]
    [InlineData(102, 278, "BottomLeft")]
    [InlineData(120, 278, "Bottom")]
    [InlineData(102, 260, "Left")]
    [InlineData(498, 278, "BottomRight")]
    [InlineData(480, 278, "Bottom")]
    [InlineData(498, 260, "Right")]
    [InlineData(98, 100, "Left")]
    [InlineData(120, 78, "Top")]
    [InlineData(300, 180, "Move")]
    [InlineData(300, 80, "Move")]
    [InlineData(50, 50, "None")]
    [InlineData(99, 180, "None")]
    [InlineData(501, 180, "None")]
    [InlineData(300, 79, "None")]
    [InlineData(300, 281, "None")]
    public void Bracket_hit_targets_follow_the_visible_arms_at_every_dpi(double x, double y, string expected)
    {
        foreach (var scale in new[] { 1d, 1.5, 2 })
        {
            var region = new ScreenRect((int)(100 * scale), (int)(80 * scale),
                (int)(400 * scale), (int)(200 * scale));

            var mode = RegionSelectionGeometry.HitTest(region, scale, x, y);

            Assert.Equal(Enum.Parse<RegionDragMode>(expected), mode);
        }
    }

    [Theory]
    [InlineData("Left", 130, 80, 370, 200)]
    [InlineData("Right", 100, 80, 430, 200)]
    [InlineData("Top", 100, 100, 400, 180)]
    [InlineData("Bottom", 100, 80, 400, 220)]
    [InlineData("TopLeft", 130, 100, 370, 180)]
    [InlineData("TopRight", 100, 100, 430, 180)]
    [InlineData("BottomLeft", 130, 80, 370, 220)]
    [InlineData("BottomRight", 100, 80, 430, 220)]
    [InlineData("Move", 130, 100, 400, 200)]
    public void Dragging_only_changes_the_requested_edges(string mode, int x, int y, int width, int height)
    {
        var result = RegionSelectionGeometry.Drag(new ScreenRect(100, 80, 400, 200),
            Enum.Parse<RegionDragMode>(mode), 30, 20, 800, 450);

        Assert.Equal(new ScreenRect(x, y, width, height), result);
    }

    [Theory]
    [InlineData(1000, 1000, 400, 250)]
    [InlineData(-1000, -1000, 0, 0)]
    public void Moving_clamps_the_entire_region_without_resizing(double dx, double dy, int x, int y)
    {
        var result = RegionSelectionGeometry.Drag(new ScreenRect(100, 80, 400, 200),
            RegionDragMode.Move, dx, dy, 800, 450);

        Assert.Equal(new ScreenRect(x, y, 400, 200), result);
    }

    [Theory]
    [InlineData("TopLeft", 1000, 1000, 460, 258, 40, 22)]
    [InlineData("BottomRight", -1000, -1000, 100, 80, 40, 22)]
    [InlineData("TopLeft", -1000, -1000, 0, 0, 500, 280)]
    [InlineData("BottomRight", 1000, 1000, 100, 80, 700, 370)]
    public void Resizing_preserves_minimum_size_and_window_bounds(string mode, double dx, double dy,
        int x, int y, int width, int height)
    {
        var result = RegionSelectionGeometry.Drag(new ScreenRect(100, 80, 400, 200),
            Enum.Parse<RegionDragMode>(mode), dx, dy, 800, 450);

        Assert.Equal(new ScreenRect(x, y, width, height), result);
    }

    [Fact]
    public void A_saved_region_clamped_near_the_window_edge_can_still_be_resized()
    {
        var result = RegionSelectionGeometry.Drag(new ScreenRect(780, 430, 20, 10),
            RegionDragMode.BottomRight, 100, 100, 800, 450);

        Assert.Equal(new ScreenRect(780, 430, 20, 20), result);
    }
}

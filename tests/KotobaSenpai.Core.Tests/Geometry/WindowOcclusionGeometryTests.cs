using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Core.Tests.Geometry;

public sealed class WindowOcclusionGeometryTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(29, false)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    [InlineData(100, false)]
    [InlineData(150, false)]
    public void Coverage_up_to_the_full_target_does_not_exceed_the_hide_threshold(int coveredWidth, bool expectedHidden)
    {
        Assert.Equal(expectedHidden, WindowOcclusionGeometry.ExceedsOcclusionThreshold(
            new ScreenRect(100, 100, 100, 100), [new ScreenRect(100, 100, coveredWidth, 100)], out _));
    }

    [Fact]
    public void Empty_outside_and_edge_touching_windows_do_not_count_as_coverage()
    {
        var target = new ScreenRect(100, 100, 100, 100);
        Assert.False(WindowOcclusionGeometry.ExceedsOcclusionThreshold(target, [], out var emptyRatio));
        Assert.Equal(0, emptyRatio);
        Assert.False(WindowOcclusionGeometry.ExceedsOcclusionThreshold(target,
            [new ScreenRect(0, 0, 50, 50), new ScreenRect(200, 100, 100, 100)], out var outsideRatio));
        Assert.Equal(0, outsideRatio);
    }

    [Fact]
    public void Multiple_small_disjoint_windows_combine_coverage_without_hiding_the_overlay()
    {
        Assert.False(WindowOcclusionGeometry.ExceedsOcclusionThreshold(new ScreenRect(100, 100, 100, 100),
            [new ScreenRect(100, 100, 20, 100), new ScreenRect(180, 100, 20, 100)], out var ratio));
        Assert.Equal(0.4, ratio, 10);
    }

    [Fact]
    public void Overlapping_and_duplicate_windows_count_their_union_only_once()
    {
        Assert.False(WindowOcclusionGeometry.ExceedsOcclusionThreshold(new ScreenRect(100, 100, 100, 100),
            [new ScreenRect(100, 100, 50, 30), new ScreenRect(125, 110, 50, 30), new ScreenRect(100, 100, 50, 30)],
            out var ratio));
        Assert.Equal(0.25, ratio, 10);
    }

    [Fact]
    public void Only_the_intersection_inside_the_target_client_counts()
    {
        Assert.False(WindowOcclusionGeometry.ExceedsOcclusionThreshold(new ScreenRect(100, 100, 100, 100),
            [new ScreenRect(0, 0, 130, 300)], out var ratio));
        Assert.Equal(0.3, ratio, 10);
    }

    [Fact]
    public void Large_clients_and_negative_coordinates_do_not_overflow_area_calculation()
    {
        Assert.False(WindowOcclusionGeometry.ExceedsOcclusionThreshold(new ScreenRect(-50000, -50000, 100000, 100000),
            [new ScreenRect(-50000, -50000, 30000, 100000)], out var ratio));
        Assert.Equal(0.3, ratio, 10);
    }

    [Fact]
    public void Occlusion_requires_positive_area_intersection()
    {
        var target = new ScreenRect(100, 100, 100, 100);

        Assert.True(WindowOcclusionGeometry.HasPositiveAreaIntersection(
            target,
            new ScreenRect(0, 0, 101, 101)));
        Assert.True(WindowOcclusionGeometry.HasPositiveAreaIntersection(
            target,
            new ScreenRect(199, 100, 50, 50)));
        Assert.False(WindowOcclusionGeometry.HasPositiveAreaIntersection(
            target,
            new ScreenRect(200, 100, 50, 50)));
        Assert.False(WindowOcclusionGeometry.HasPositiveAreaIntersection(
            target,
            new ScreenRect(0, 0, 50, 50)));
    }
}

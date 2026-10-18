using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class TargetWindowTrackerTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(29, false)]
    [InlineData(30, false)]
    [InlineData(31, true)]
    [InlineData(100, true)]
    [InlineData(150, true)]
    public void Only_coverage_strictly_above_thirty_percent_hides_the_overlay(int coveredWidth, bool expectedHidden)
    {
        Assert.Equal(expectedHidden, WinEventTargetWindowTracker.ExceedsOcclusionThreshold(
            new ScreenRect(100, 100, 100, 100), [new ScreenRect(100, 100, coveredWidth, 100)], out _));
    }

    [Fact]
    public void Empty_outside_and_edge_touching_windows_do_not_count_as_coverage()
    {
        var target = new ScreenRect(100, 100, 100, 100);
        Assert.False(WinEventTargetWindowTracker.ExceedsOcclusionThreshold(target, [], out var emptyRatio));
        Assert.Equal(0, emptyRatio);
        Assert.False(WinEventTargetWindowTracker.ExceedsOcclusionThreshold(target,
            [new ScreenRect(0, 0, 50, 50), new ScreenRect(200, 100, 100, 100)], out var outsideRatio));
        Assert.Equal(0, outsideRatio);
    }

    [Fact]
    public void Multiple_small_disjoint_windows_can_exceed_the_threshold_together()
    {
        Assert.True(WinEventTargetWindowTracker.ExceedsOcclusionThreshold(new ScreenRect(100, 100, 100, 100),
            [new ScreenRect(100, 100, 20, 100), new ScreenRect(180, 100, 20, 100)], out var ratio));
        Assert.Equal(0.4, ratio, 10);
    }

    [Fact]
    public void Overlapping_and_duplicate_windows_count_their_union_only_once()
    {
        Assert.False(WinEventTargetWindowTracker.ExceedsOcclusionThreshold(new ScreenRect(100, 100, 100, 100),
            [new ScreenRect(100, 100, 50, 30), new ScreenRect(125, 110, 50, 30), new ScreenRect(100, 100, 50, 30)],
            out var ratio));
        Assert.Equal(0.25, ratio, 10);
    }

    [Fact]
    public void Only_the_intersection_inside_the_target_client_counts()
    {
        Assert.False(WinEventTargetWindowTracker.ExceedsOcclusionThreshold(new ScreenRect(100, 100, 100, 100),
            [new ScreenRect(0, 0, 130, 300)], out var ratio));
        Assert.Equal(0.3, ratio, 10);
    }

    [Fact]
    public void Large_clients_and_negative_coordinates_do_not_overflow_area_calculation()
    {
        Assert.False(WinEventTargetWindowTracker.ExceedsOcclusionThreshold(new ScreenRect(-50000, -50000, 100000, 100000),
            [new ScreenRect(-50000, -50000, 30000, 100000)], out var ratio));
        Assert.Equal(0.3, ratio, 10);
    }

    [Theory]
    [InlineData(0x2800A8L)]
    [InlineData(0x80020L)]
    public void Click_through_layered_helper_bounds_are_not_solid_occluders(long extendedStyle)
    {
        Assert.False(WinEventTargetWindowTracker.CanOcclude(extendedStyle));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(0x200100L)]
    [InlineData(0x80000L)]
    [InlineData(0x80L)]
    public void Normal_layered_and_tool_windows_still_occlude(long extendedStyle)
    {
        Assert.True(WinEventTargetWindowTracker.CanOcclude(extendedStyle));
    }

    [Fact]
    public void Foreground_events_are_relevant_even_when_the_event_hwnd_is_another_window()
    {
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x0003, (nint)2, (nint)1, 99, 99));
    }

    [Fact]
    public void Window_events_are_relevant_for_target_and_occluding_windows()
    {
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)1, (nint)1, 0, 0));
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)2, (nint)1, 0, 0));
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x8002, (nint)2, (nint)1, 0, 0));
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x8001, (nint)2, (nint)1, 0, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(
            0x800B,
            (nint)2,
            (nint)1,
            0,
            0,
            new HashSet<nint> { (nint)2 }));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)1, (nint)1, 1, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)1, (nint)1, 0, 1));
    }

    [Fact]
    public void Reorder_events_are_relevant_for_z_order_changes()
    {
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x8004, (nint)2, (nint)1, 0, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x8004, (nint)2, (nint)1, 1, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x8004, (nint)2, (nint)1, 0, 1));
    }

    [Fact]
    public void Invalid_target_is_reported_uncapturable_and_can_be_detached()
    {
        using var tracker = new WinEventTargetWindowTracker();
        var target = new WindowTarget((nint)int.MaxValue, "missing", new ScreenRect(0, 0, 100, 100));

        var snapshot = tracker.Attach(target);

        Assert.False(snapshot.IsValid);
        Assert.False(snapshot.IsCapturable);
        tracker.Detach();
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Occlusion_requires_positive_area_intersection()
    {
        var target = new ScreenRect(100, 100, 100, 100);

        Assert.True(WinEventTargetWindowTracker.HasPositiveAreaIntersection(
            target,
            new ScreenRect(0, 0, 101, 101)));
        Assert.True(WinEventTargetWindowTracker.HasPositiveAreaIntersection(
            target,
            new ScreenRect(199, 100, 50, 50)));
        Assert.False(WinEventTargetWindowTracker.HasPositiveAreaIntersection(
            target,
            new ScreenRect(200, 100, 50, 50)));
        Assert.False(WinEventTargetWindowTracker.HasPositiveAreaIntersection(
            target,
            new ScreenRect(0, 0, 50, 50)));
    }
}

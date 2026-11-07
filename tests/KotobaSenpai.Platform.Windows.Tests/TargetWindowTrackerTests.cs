using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class TargetWindowTrackerTests
{
    [Theory]
    [InlineData(10u, 20u, false, true)]
    [InlineData(11u, 20u, false, false)]
    [InlineData(10u, 21u, false, false)]
    [InlineData(10u, 20u, true, false)]
    public void Reused_or_destroyed_window_identity_is_not_the_selected_target(uint thread, uint process, bool destroyed, bool expected)
        => Assert.Equal(expected, TargetWindowSnapshotReader.IsSameIdentity(10, 20, thread, process, destroyed));

    [Theory]
    [InlineData(0x2800A8L)]
    [InlineData(0x80020L)]
    public void Click_through_layered_helper_bounds_are_not_solid_occluders(long extendedStyle)
    {
        Assert.False(WindowOcclusionEvaluator.CanOcclude(extendedStyle));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(0x200100L)]
    [InlineData(0x80000L)]
    [InlineData(0x80L)]
    public void Normal_layered_and_tool_windows_still_occlude(long extendedStyle)
    {
        Assert.True(WindowOcclusionEvaluator.CanOcclude(extendedStyle));
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
        using var tracker = new WinEventTargetWindowTracker(new InlineUiThread());
        var target = new WindowTarget((nint)int.MaxValue, "missing", new ScreenRect(0, 0, 100, 100));

        var snapshot = tracker.Attach(target);

        Assert.False(snapshot.IsValid);
        Assert.False(snapshot.IsCapturable);
        tracker.Detach();
        Assert.Null(tracker.Current);
    }
}

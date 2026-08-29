using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class TargetWindowTrackerTests
{
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

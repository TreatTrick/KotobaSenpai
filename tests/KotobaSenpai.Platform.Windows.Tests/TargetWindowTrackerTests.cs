using KotobaSenpai.Platform.Windows.Overlay;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class TargetWindowTrackerTests
{
    [Fact]
    public void Foreground_events_are_relevant_even_when_the_event_hwnd_is_another_window()
    {
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x0003, (nint)2, (nint)1, 99, 99));
    }

    [Fact]
    public void Object_events_are_relevant_only_for_the_target_window_object()
    {
        Assert.True(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)1, (nint)1, 0, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)2, (nint)1, 0, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)1, (nint)1, 1, 0));
        Assert.False(WinEventTargetWindowTracker.IsRelevantEvent(0x800B, (nint)1, (nint)1, 0, 1));
    }

    [Fact]
    public void Invalid_target_is_reported_uncapturable_and_can_be_detached()
    {
        using var tracker = new WinEventTargetWindowTracker();
        var target = new WindowTarget((nint)int.MaxValue, "missing", new ScreenRect(0, 0, 100, 100));

        var snapshot = tracker.Attach(target);

        Assert.False(snapshot.IsCapturable);
        tracker.Detach();
        Assert.Null(tracker.Current);
    }
}

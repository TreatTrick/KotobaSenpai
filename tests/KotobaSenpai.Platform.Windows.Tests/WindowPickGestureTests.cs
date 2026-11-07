using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class WindowPickGestureTests
{
    [Fact]
    public void Escape_waits_for_a_swallowed_mouse_press_to_be_released()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.Press(target);
        gesture.PressEscape();
        Assert.False(gesture.ReleaseEscape());
        Assert.False(gesture.IsCancellationReady);
        Assert.Null(gesture.Release(target));
        Assert.True(gesture.IsCancellationReady);
    }

    [Fact]
    public void A_click_while_escape_is_held_waits_for_escape_release_without_committing()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.PressEscape();
        gesture.Press(target);
        Assert.Null(gesture.Release(target));
        Assert.False(gesture.IsCancellationReady);
        Assert.True(gesture.ReleaseEscape());
    }

    [Fact]
    public void A_click_commits_only_after_release_over_the_same_window_identity()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.Press(target);

        Assert.Null(gesture.Release(Candidate(11, 20, 30)));
        Assert.Null(gesture.Release(target));

        gesture.Press(target);
        var moved = target with { Target = target.Target.WithBounds(new ScreenRect(-300, 40, 120, 80)) };
        Assert.Same(moved, gesture.Release(moved));
        Assert.Null(gesture.Release(moved));
    }

    [Theory]
    [InlineData(21, 30)]
    [InlineData(20, 31)]
    public void A_reused_handle_cannot_complete_the_original_click(uint processId, uint threadId)
    {
        var gesture = new WindowPickGesture();
        gesture.Press(Candidate(10, 20, 30));

        Assert.Null(gesture.Release(Candidate(10, processId, threadId)));
    }

    [Fact]
    public void An_invalid_press_or_release_does_not_select_a_window()
    {
        var gesture = new WindowPickGesture();
        var target = Candidate(10, 20, 30);
        gesture.Press(null);
        Assert.Null(gesture.Release(target));
        gesture.Press(target);
        Assert.Null(gesture.Release(null));
        Assert.Null(gesture.Release(target));
    }

    private static WindowPickerCandidate Candidate(int handle, uint processId, uint threadId)
    {
        var bounds = new ScreenRect(-100, 20, 120, 80);
        return new(new WindowTarget((nint)handle, "Target", bounds), bounds, processId, threadId, 96);
    }
}

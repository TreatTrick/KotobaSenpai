using System.Windows;
using KotobaSenpai.App.Support;

namespace KotobaSenpai.App.Tests;

public sealed class MascotPlaybackTests
{
    [Fact]
    public void Only_the_popup_upper_right_sixteenth_can_play_towards_sadness()
    {
        var playback = new MascotPlayback();
        var window = new Rect(0, 0, 480, 480);
        var close = new Rect(440, 12, 24, 24);
        playback.FollowPopup(new Point(370, 30), close, window);
        playback.FollowPopup(new Point(400, 30), close, window);
        Assert.Equal(-1, playback.Direction);
        playback.FollowPopup(new Point(200, 200), close, window);
        Assert.Equal(1, playback.Direction);
        playback.FollowPopup(new Point(400, -10), close, window);
        Assert.Equal(1, playback.Direction);
    }

    [Fact]
    public void Mouse_distance_accumulates_three_DIP_and_window_motion_does_not_reverse_playback()
    {
        var playback = new MascotPlayback();
        var target = new Rect(100, 100, 20, 20);
        playback.Follow(new Point(50, 110), target, true);
        playback.Follow(new Point(51, 110), target, true);
        Assert.Equal(-1, playback.Direction);
        playback.Follow(new Point(53, 110), target, true);
        Assert.Equal(1, playback.Direction);
        playback.Follow(new Point(53, 110), new Rect(200, 100, 20, 20), true);
        Assert.Equal(1, playback.Direction);
        playback.Follow(new Point(49, 110), target, true);
        Assert.Equal(-1, playback.Direction);
    }

    [Fact]
    public void Playback_uses_elapsed_time_clamps_stalls_and_stops_at_mood_endpoints()
    {
        var playback = new MascotPlayback();
        playback.Direction = 1;
        playback.Advance(1);
        Assert.Equal(2, playback.Frame);
        for (var i = 0; i < 100; i++) playback.Advance(0.1);
        Assert.Equal(120, playback.Frame);
        playback.Direction = -1;
        for (var i = 0; i < 100; i++) playback.Advance(0.1);
        Assert.Equal(0, playback.Frame);
    }

    [Fact]
    public void Celebration_finishes_A_then_loops_B_and_reset_restores_following()
    {
        var playback = new MascotPlayback();
        playback.Celebrate();
        Assert.Equal(MascotPhase.CheeringUp, playback.Phase);
        for (var i = 0; i < 51; i++) playback.Advance(0.1);
        Assert.Equal(MascotPhase.Celebrating, playback.Phase);
        Assert.Equal("B", playback.Clip);
        for (var i = 0; i < 60; i++) playback.Advance(0.1);
        Assert.InRange(playback.Frame, 0, 120);
        playback.Reset(false);
        Assert.Equal(MascotPhase.FollowingMouse, playback.Phase);
        Assert.Equal("A", playback.Clip);
        Assert.Equal(0, playback.Frame);
        playback.Reset(true);
        Assert.Equal("B", playback.Clip);
    }
}

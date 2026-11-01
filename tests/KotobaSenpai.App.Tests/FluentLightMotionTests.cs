using System.Windows;
using KotobaSenpai.App.Controls;

namespace KotobaSenpai.App.Tests;

public sealed class FluentLightMotionTests
{
    [Fact]
    public void One_pointer_lights_both_controls_across_their_gap()
    {
        var pointer = new Point(105, 20);
        var left = FluentLightMotion.Proximity(new Rect(0, 0, 100, 40), pointer);
        var right = FluentLightMotion.Proximity(new Rect(110, 0, 100, 40), pointer);

        Assert.InRange(left, 0.9, 1);
        Assert.Equal(left, right);
        Assert.Equal(0, FluentLightMotion.Proximity(new Rect(400, 0, 100, 40), pointer));
    }

    [Fact]
    public void Moving_the_pointer_preserves_continuity_and_is_independent_of_frame_rate()
    {
        var slowFrames = CreateMovingLight();
        var fastFrames = CreateMovingLight();

        Assert.Equal(new Point(20, 30), slowFrames.Position);
        slowFrames.Advance(0.032, animate: true);
        fastFrames.Advance(0.016, animate: true);
        fastFrames.Advance(0.016, animate: true);

        Assert.InRange(slowFrames.Position.X, 20.1, 219.9);
        Assert.Equal(slowFrames.Position.X, fastFrames.Position.X, 8);
        Assert.Equal(slowFrames.Position.Y, fastFrames.Position.Y, 8);
    }

    [Fact]
    public void Leaving_fades_at_the_last_position_and_eventually_stops_rendering()
    {
        var light = CreateMovingLight();
        for (var i = 0; i < 120; i++) light.Advance(1d / 60, animate: true);
        Assert.True(light.IsSettled);
        Assert.Equal(new Point(220, 130), light.Position);
        Assert.Equal(1, light.Presence);

        light.SetPointer(null);
        light.Advance(0.016, animate: true);
        Assert.InRange(light.Presence, 0.01, 0.99);
        Assert.Equal(new Point(220, 130), light.Position);
        Assert.False(light.IsSettled);

        for (var i = 0; i < 120; i++) light.Advance(1d / 60, animate: true);
        Assert.Equal(0, light.Presence);
        Assert.True(light.IsSettled);
    }

    [Fact]
    public void Reduced_motion_applies_pointer_and_visibility_without_interpolation()
    {
        var light = CreateMovingLight();
        light.Advance(0.001, animate: false);
        Assert.Equal(new Point(220, 130), light.Position);
        Assert.Equal(1, light.Presence);
        Assert.True(light.IsSettled);

        light.SetPointer(null);
        light.Advance(0.001, animate: false);
        Assert.Equal(0, light.Presence);
        Assert.True(light.IsSettled);
    }

    private static FluentLightMotion CreateMovingLight()
    {
        var light = new FluentLightMotion();
        light.SetPointer(new Point(20, 30));
        light.Advance(1, animate: false);
        light.SetPointer(new Point(220, 130));
        return light;
    }
}

using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhrasePopupHoverTrackerTests
{
    private static readonly ScreenRect Bounds = new(100, 100, 200, 150);

    [Theory]
    [InlineData(150, 50, 150, 70)]
    [InlineData(150, 300, 150, 280)]
    [InlineData(50, 150, 70, 150)]
    [InlineData(350, 150, 330, 150)]
    [InlineData(50, 50, 70, 70)]
    public void Approaching_an_edge_or_corner_keeps_the_popup_open(int startX, int startY, int nextX, int nextY)
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, startX, startY, 1, isOverSource: true));
        Assert.True(tracker.ShouldRemainOpen(Bounds, nextX, nextY, 1, isOverSource: false));
    }

    [Theory]
    [InlineData(150, 50, 150, 40)]
    [InlineData(150, 300, 150, 310)]
    [InlineData(50, 150, 40, 150)]
    [InlineData(350, 150, 360, 150)]
    public void Retreating_after_leaving_the_source_closes_the_popup(int startX, int startY, int nextX, int nextY)
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, startX, startY, 1, isOverSource: true));
        Assert.False(tracker.ShouldRemainOpen(Bounds, nextX, nextY, 1, isOverSource: false));
    }

    [Fact]
    public void Staying_still_or_moving_parallel_to_an_edge_does_not_dismiss()
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 50, 1, isOverSource: true));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 50, 1, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 180, 50, 1, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 180, 80, 1, isOverSource: false));
        Assert.False(tracker.ShouldRemainOpen(Bounds, 180, 79, 1, isOverSource: false));
    }

    [Theory]
    [InlineData(90, 150, 89, 150)]
    [InlineData(310, 150, 311, 150)]
    [InlineData(150, 90, 150, 89)]
    [InlineData(150, 260, 150, 261)]
    [InlineData(306, 258, 307, 258)]
    public void Entry_requires_moving_more_than_ten_dip_from_the_nearest_edge_to_close(int nearX, int nearY, int farX, int farY)
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 150, 1, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(Bounds, nearX, nearY, 1, isOverSource: false));
        Assert.False(tracker.ShouldRemainOpen(Bounds, farX, farY, 1, isOverSource: true));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(1.25, 12)]
    [InlineData(1.5, 15)]
    [InlineData(2, 20)]
    public void Exit_margin_uses_current_popup_dpi(double scale, int physicalMargin)
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 150, scale, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(Bounds, Bounds.Right + physicalMargin, 150, scale, isOverSource: false));
        Assert.False(tracker.ShouldRemainOpen(Bounds, Bounds.Right + physicalMargin + 1, 150, scale, isOverSource: false));
    }

    [Fact]
    public void Changing_dpi_does_not_reuse_a_stale_margin()
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 150, 2, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 320, 150, 2, isOverSource: false));
        Assert.False(tracker.ShouldRemainOpen(Bounds, 320, 150, 1, isOverSource: false));
    }

    [Fact]
    public void Hovering_the_source_before_entry_keeps_the_popup_open_while_moving_away()
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 50, 1, isOverSource: true));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 40, 1, isOverSource: true));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 60, 1, isOverSource: false));
    }

    [Fact]
    public void Reset_discards_entry_and_distance_from_previous_content()
    {
        var tracker = new PhrasePopupHoverTracker();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 150, 1, isOverSource: false));
        tracker.Reset();
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 40, 1, isOverSource: true));
        Assert.True(tracker.ShouldRemainOpen(Bounds, 150, 50, 1, isOverSource: false));
        Assert.False(tracker.ShouldRemainOpen(Bounds, 150, 49, 1, isOverSource: false));
    }

    [Fact]
    public void Negative_monitor_coordinates_use_the_same_distance_rules()
    {
        var tracker = new PhrasePopupHoverTracker();
        var bounds = new ScreenRect(-400, -300, 200, 150);
        Assert.True(tracker.ShouldRemainOpen(bounds, -450, -200, 1.5, isOverSource: true));
        Assert.True(tracker.ShouldRemainOpen(bounds, -430, -200, 1.5, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(bounds, -350, -200, 1.5, isOverSource: false));
        Assert.True(tracker.ShouldRemainOpen(bounds, -415, -200, 1.5, isOverSource: false));
        Assert.False(tracker.ShouldRemainOpen(bounds, -416, -200, 1.5, isOverSource: false));
    }
}

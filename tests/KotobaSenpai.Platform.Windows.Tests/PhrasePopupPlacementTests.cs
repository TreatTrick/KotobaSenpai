using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhrasePopupPlacementTests
{
    [Theory]
    [InlineData(360, 200, 4)]
    [InlineData(450, 250, 5)]
    [InlineData(720, 400, 8)]
    public void Scaled_popups_keep_the_anchor_in_physical_pixels(int width, int height, int gap)
    {
        var anchor = new ScreenRect(1000, 600, 100, 40);
        var workArea = new ScreenRect(0, 0, 3440, 1400);

        var placement = PhrasePopup.CalculatePlacement(anchor, workArea, width, height, gap);

        Assert.Equal(new ScreenRect(1100 + gap, 600, width, height), placement);
    }

    [Fact]
    public void Popup_near_the_bottom_right_moves_left_and_stays_on_screen()
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(3330, 1320, 100, 40), new ScreenRect(0, 0, 3440, 1400), 720, 480, 8);

        Assert.Equal(new ScreenRect(2602, 920, 720, 480), placement);
    }

    [Fact]
    public void Popup_uses_the_selected_monitor_with_negative_coordinates()
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(-250, 980, 80, 40), new ScreenRect(-1920, 0, 1920, 1040), 360, 200, 4);

        Assert.Equal(new ScreenRect(-614, 840, 360, 200), placement);
    }

    [Fact]
    public void Popup_larger_than_the_work_area_is_limited_to_two_thirds_height()
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(-10, -80, 10, 30), new ScreenRect(-800, -600, 800, 560), 900, 900, 8);

        Assert.Equal(new ScreenRect(-800, -461, 800, 373), placement);
    }

    [Theory]
    [InlineData(200, 308)]
    [InlineData(900, 492)]
    public void Popup_chooses_the_horizontal_side_with_more_room(int anchorX, int expectedX)
    {
        var anchor = new ScreenRect(anchorX, 300, 100, 40);
        var placement = PhrasePopup.CalculatePlacement(anchor, new ScreenRect(0, 0, 1200, 900), 400, 300, 8);

        Assert.Equal(new ScreenRect(expectedX, 300, 400, 300), placement);
        Assert.True(placement.Right <= anchor.X - 8 || placement.X >= anchor.Right + 8);
    }

    [Theory]
    [InlineData(80, 128)]
    [InlineData(700, 392)]
    public void Popup_uses_vertical_space_when_neither_horizontal_side_fits(int anchorY, int expectedY)
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(250, anchorY, 100, 40), new ScreenRect(0, 0, 600, 900), 440, 300, 8);

        Assert.Equal(new ScreenRect(160, expectedY, 440, 300), placement);
    }

    [Fact]
    public void Vertical_fallback_reduces_height_instead_of_covering_the_source()
    {
        var anchor = new ScreenRect(250, 400, 100, 40);
        var placement = PhrasePopup.CalculatePlacement(anchor, new ScreenRect(0, 0, 600, 900), 440, 900, 8);

        Assert.Equal(new ScreenRect(160, 448, 440, 452), placement);
    }

    [Theory]
    [InlineData(900, 600)]
    [InlineData(1040, 693)]
    [InlineData(1400, 933)]
    public void Side_popup_height_never_exceeds_two_thirds_of_the_work_area(int workHeight, int expectedHeight)
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(100, 300, 100, 40), new ScreenRect(0, 0, 1920, workHeight), 440, workHeight, 8);

        Assert.Equal(new ScreenRect(208, Math.Min(300, workHeight - expectedHeight), 440, expectedHeight), placement);
    }

    [Theory]
    [InlineData(-500, 0)]
    [InlineData(2000, 760)]
    public void Off_screen_anchors_keep_the_popup_inside_the_nearest_work_area(int anchorX, int expectedX)
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(anchorX, 300, 100, 40), new ScreenRect(0, 0, 1200, 900), 440, 300, 8);

        Assert.Equal(new ScreenRect(expectedX, 300, 440, 300), placement);
    }
}

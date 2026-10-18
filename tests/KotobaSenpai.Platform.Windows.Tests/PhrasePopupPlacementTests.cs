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

        Assert.Equal(new ScreenRect(1000, 640 + gap, width, height), placement);
    }

    [Fact]
    public void Popup_near_the_bottom_right_moves_above_and_stays_on_screen()
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(3330, 1320, 100, 40), new ScreenRect(0, 0, 3440, 1400), 720, 480, 8);

        Assert.Equal(new ScreenRect(2720, 832, 720, 480), placement);
    }

    [Fact]
    public void Popup_uses_the_selected_monitor_with_negative_coordinates()
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(-250, 980, 80, 40), new ScreenRect(-1920, 0, 1920, 1040), 360, 200, 4);

        Assert.Equal(new ScreenRect(-360, 776, 360, 200), placement);
    }

    [Fact]
    public void Popup_larger_than_the_work_area_is_limited_on_both_axes()
    {
        var placement = PhrasePopup.CalculatePlacement(
            new ScreenRect(-10, -80, 10, 30), new ScreenRect(-800, -600, 800, 560), 900, 900, 8);

        Assert.Equal(new ScreenRect(-800, -600, 800, 560), placement);
    }
}

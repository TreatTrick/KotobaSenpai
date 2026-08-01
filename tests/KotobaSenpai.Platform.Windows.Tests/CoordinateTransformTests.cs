using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class CoordinateTransformTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void Mapping_is_stable_across_dpi_scales(double dpi)
    {
        const int frameWidth = 800;
        const int frameHeight = 600;
        var window = new ScreenRect(-1280, 40, (int)(800 * dpi), (int)(600 * dpi));
        var word = new OcrWord("語", new ScreenRect(80, 60, 160, 30));

        var mapped = CoordinateMapper.ToScreen(word, frameWidth, frameHeight, window);

        Assert.Equal(window.X + (int)Math.Round(80 * dpi), mapped.X);
        Assert.Equal(window.Y + (int)Math.Round(60 * dpi), mapped.Y);
        Assert.Equal((int)Math.Round(160 * dpi), mapped.Width);
        Assert.Equal((int)Math.Round(30 * dpi), mapped.Height);
    }
}

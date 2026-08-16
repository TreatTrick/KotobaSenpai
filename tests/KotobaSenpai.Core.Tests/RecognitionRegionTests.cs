using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Tests;

public sealed class RecognitionRegionTests
{
    [Fact]
    public void Full_region_maps_to_whole_window()
    {
        var rect = RecognitionRegion.Full.ToPixelRect(100, 80);
        Assert.Equal(new ScreenRect(0, 0, 100, 80), rect);
    }

    [Fact]
    public void Normalized_region_maps_to_pixel_rect()
    {
        var region = new RecognitionRegion(0.25, 0.5, 0.5, 0.25);
        var rect = region.ToPixelRect(200, 100);
        Assert.Equal(new ScreenRect(50, 50, 100, 25), rect);
    }

    [Fact]
    public void Pixel_rect_roundtrips_to_normalized()
    {
        var rect = new ScreenRect(20, 10, 60, 40);
        var region = RecognitionRegion.FromPixelRect(rect, 200, 100);
        Assert.Equal(0.1, region.X, 6);
        Assert.Equal(0.1, region.Y, 6);
        Assert.Equal(0.3, region.Width, 6);
        Assert.Equal(0.4, region.Height, 6);
    }

    [Fact]
    public void Rejects_region_outside_unit_square_or_degenerate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecognitionRegion(0.5, 0.5, 0.6, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecognitionRegion(0, 0, 0, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecognitionRegion(-0.1, 0, 0.5, 0.5));
    }

    [Fact]
    public void Serialize_and_parse_roundtrip()
    {
        var region = new RecognitionRegion(0.25, 0.5, 0.5, 0.25);
        Assert.True(RecognitionRegion.TryParse(region.Serialize(), out var parsed));
        Assert.Equal(region, parsed);
    }

    [Fact]
    public void TryParse_rejects_malformed()
    {
        Assert.False(RecognitionRegion.TryParse("1,2,3", out _));
        Assert.False(RecognitionRegion.TryParse("a,b,c,d", out _));
        Assert.False(RecognitionRegion.TryParse("0.5,0.5,0.6,0.1", out _)); // out of unit square
        Assert.False(RecognitionRegion.TryParse(null, out _));
    }
}
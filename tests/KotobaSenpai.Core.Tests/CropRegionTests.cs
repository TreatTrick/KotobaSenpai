using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

public sealed class CropRegionTests
{
    [Fact]
    public void Resolve_returns_null_when_unset_or_degenerate()
    {
        Assert.Null(CropRegion.Resolve(null, 100, 100));
        // Tiny relative to the window (1/20 floor) -> fall back to full window.
        Assert.Null(CropRegion.Resolve(new ScreenRect(0, 0, 2, 2), 100, 100));
    }

    [Fact]
    public void Resolve_clamps_to_window()
    {
        var rect = CropRegion.Resolve(new ScreenRect(-5, -5, 200, 200), 100, 100);
        Assert.NotNull(rect);
        Assert.Equal(new ScreenRect(0, 0, 100, 100), rect);
    }

    [Fact]
    public void Resolve_passes_through_valid_region()
    {
        var rect = CropRegion.Resolve(new ScreenRect(10, 20, 50, 30), 100, 100);
        Assert.Equal(new ScreenRect(10, 20, 50, 30), rect);
    }
}
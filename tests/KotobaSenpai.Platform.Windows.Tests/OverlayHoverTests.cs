using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class OverlayHoverTests
{
    [Theory]
    [InlineData("reorder")]
    [InlineData("foreground")]
    [InlineData("title")]
    public void Non_geometry_window_events_preserve_the_hover_popup(string change)
    {
        var rendered = new TargetWindowSnapshot((nint)42, "Target", new ScreenRect(100, 100, 800, 600), 1, true, false, true);
        var current = change switch
        {
            "foreground" => rendered with { IsForeground = false },
            "title" => rendered with { Title = "Updated title" },
            _ => rendered with { }
        };

        Assert.False(WpfOverlayRenderer.RequiresGeometryRender(rendered, current, isVisible: true));
    }

    [Fact]
    public void Geometry_changes_and_restoring_a_hidden_overlay_still_rebuild_it()
    {
        var rendered = new TargetWindowSnapshot((nint)42, "Target", new ScreenRect(100, 100, 800, 600), 1, true, false, true);

        Assert.True(WpfOverlayRenderer.RequiresGeometryRender(null, rendered, isVisible: true));
        Assert.True(WpfOverlayRenderer.RequiresGeometryRender(rendered, rendered, isVisible: false));
        Assert.True(WpfOverlayRenderer.RequiresGeometryRender(rendered,
            rendered with { Bounds = new ScreenRect(200, 100, 800, 600) }, isVisible: true));
        Assert.True(WpfOverlayRenderer.RequiresGeometryRender(rendered,
            rendered with { Bounds = new ScreenRect(100, 100, 1000, 700) }, isVisible: true));
        Assert.True(WpfOverlayRenderer.RequiresGeometryRender(rendered,
            rendered with { DpiScale = 1.25 }, isVisible: true));
    }

    [Fact]
    public void Group_hover_maps_one_cross_line_word_to_all_underline_indices()
    {
        var lineOwners = new[] { 0, 0, 1 };

        var lineIndices = WpfOverlayRenderer.GetLineIndicesForWords([0], lineOwners);

        Assert.Equal([0, 1], lineIndices);
    }

    [Fact]
    public void Group_hover_maps_all_member_words_without_using_word_indices_as_line_indices()
    {
        var lineOwners = new[] { 0, 0, 1, 2 };

        var lineIndices = WpfOverlayRenderer.GetLineIndicesForWords([0, 2], lineOwners);

        Assert.Equal([0, 1, 3], lineIndices);
    }
}

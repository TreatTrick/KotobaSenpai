using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class OverlayHoverTests
{
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

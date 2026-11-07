using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Core.Tests.Overlay;

public sealed class WordHoverResolverTests
{
    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(140, 120, 0)]
    [InlineData(200, 150, 0)]
    [InlineData(240, 170, 0)]
    [InlineData(170, 135, -1)]
    [InlineData(99, 110, -1)]
    [InlineData(241, 160, -1)]
    public void Wrapped_word_hits_its_source_rectangles_including_edges_but_not_the_gap(int x, int y, int expected)
    {
        var word = Word(new ScreenRect(100, 100, 40, 20), new ScreenRect(200, 150, 40, 20));

        Assert.Equal(expected, WordHoverResolver.Resolve([word], x, y));
    }

    [Theory]
    [InlineData(130, 0)]
    [InlineData(170, 1)]
    public void Overlapping_words_choose_the_nearest_union_center(int x, int expected)
    {
        var words = new[] { Word(new ScreenRect(100, 100, 100, 40)), Word(new ScreenRect(120, 100, 100, 40)) };

        Assert.Equal(expected, WordHoverResolver.Resolve(words, x, 120));
    }

    [Fact]
    public void Equal_distance_preserves_word_order()
    {
        var word = Word(new ScreenRect(100, 100, 100, 40));

        Assert.Equal(0, WordHoverResolver.Resolve([word, word], 150, 120));
    }

    [Fact]
    public void Empty_sources_have_no_hover_target()
        => Assert.Equal(-1, WordHoverResolver.Resolve([], 100, 100));

    private static GroupedWord Word(params ScreenRect[] rects)
        => new(new Token("Word", "Word", "Word", "", "", "", new PartsOfSpeech("Noun", "", "", ""), "", "", "", 0), rects);
}

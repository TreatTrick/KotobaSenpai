using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Tests;

public sealed class FuriganaLayoutTests
{
    [Fact]
    public void Wrapped_name_keeps_reading_above_each_ocr_line_at_its_own_height()
    {
        var first = new ScreenRect(1930, 1123, 51, 61);
        var second = new ScreenRect(148, 1205, 50, 57);
        var word = Word("晶也", "マサヤ", first, second, firstSurfaceLength: 1);

        var parts = FuriganaLayout.GetParts(word, 0, 0, 2, 3);

        Assert.Equal([new FuriganaLinePart(first, 0, 2), new FuriganaLinePart(second, 2, 1)], parts);
        var morae = PitchAccent.SplitMorae(Kana.ToHiragana(word.Reading));
        Assert.Equal(["まさ", "や"], parts.Select(part => string.Concat(morae.Skip(part.MoraStart).Take(part.MoraCount))));
    }

    [Fact]
    public void Single_line_surface_slice_preserves_existing_position_and_reading()
    {
        var token = Token("受け入れ", "ウケイレ");
        var word = new GroupedWord(token, new ScreenRect(100, 200, 200, 40));

        Assert.Equal([new FuriganaLinePart(new ScreenRect(200, 200, 50, 40), 0, 1)],
            FuriganaLayout.GetParts(word, 0, 2, 1, 1));
    }

    [Fact]
    public void Later_source_token_keeps_its_own_line_geometry_in_a_merged_word()
    {
        var first = Token("受け", "ウケ");
        var second = Token("入れ", "イレ", 2);
        var bounds = new ScreenRect(100, 300, 100, 40);
        var word = new GroupedWord(new LookupSpan([first, second], "受け入れ", []),
            [new ScreenRect(700, 200, 100, 40), bounds],
            [
                [new SourceTokenLineGeometry(0, 0, 2, new ScreenRect(700, 200, 100, 40))],
                [new SourceTokenLineGeometry(1, 0, 2, bounds)]
            ]);

        Assert.Equal([new FuriganaLinePart(new ScreenRect(100, 300, 50, 40), 0, 1)],
            FuriganaLayout.GetParts(word, 1, 0, 1, 1));
    }

    [Fact]
    public void Wrapped_reading_slices_preserve_pitch_indices_and_small_kana_morae()
    {
        var first = new ScreenRect(800, 100, 40, 40);
        var second = new ScreenRect(100, 160, 40, 40);
        var word = Word("東京", "トウキョウ", first, second, 1);
        var pattern = PitchAccent.CreatePattern("とうきょう", 3)!;
        var parts = FuriganaLayout.GetParts(word, 0, 0, 2, pattern.Morae.Count);

        Assert.Equal(["とう", "きょう"], parts.Select(part => string.Concat(pattern.Morae.Skip(part.MoraStart).Take(part.MoraCount))));
        Assert.Equal([false, true], pattern.HighMorae.Skip(parts[0].MoraStart).Take(parts[0].MoraCount));
        Assert.Equal([true, false], pattern.HighMorae.Skip(parts[1].MoraStart).Take(parts[1].MoraCount));
    }

    [Fact]
    public void A_kana_mora_split_by_wrapping_gets_one_marker_on_its_starting_line()
    {
        var first = new ScreenRect(800, 100, 40, 40);
        var second = new ScreenRect(100, 160, 80, 40);
        var word = Word("きょあ", "キョア", first, second, 1);

        Assert.Equal([new FuriganaLinePart(first, 0, 1)], FuriganaLayout.GetParts(word, 0, 0, 2, 1));
        Assert.Equal([new FuriganaLinePart(new ScreenRect(140, 160, 40, 40), 0, 1)],
            FuriganaLayout.GetParts(word, 0, 2, 1, 1));
    }

    [Fact]
    public void Span_furigana_unions_unequal_source_boxes_on_the_same_line()
    {
        var first = new ScreenRect(220, 160, 32, 36);
        var second = new ScreenRect(257, 158, 94, 40);
        var union = new ScreenRect(220, 158, 131, 40);
        var word = new GroupedWord(new LookupSpan([Token("一", "イチ"), Token("週間", "シュウカン", 1)],
            "一週間", [], "いっしゅうかん"), [union],
            [[new(0, 0, 1, first)], [new(0, 0, 2, second)]]);

        var morae = PitchAccent.SplitMorae(word.Reading);
        Assert.Equal([new FuriganaLinePart(union, 0, morae.Count)],
            FuriganaLayout.GetSpanParts(word, 0, 3, morae.Count));
    }

    [Fact]
    public void Span_furigana_splits_whole_morae_at_the_ocr_line_break_inside_a_source_token()
    {
        var first = new ScreenRect(780, 100, 34, 39);
        var second = new ScreenRect(819, 100, 53, 39);
        var third = new ScreenRect(100, 171, 46, 32);
        var firstLine = new ScreenRect(780, 100, 92, 39);
        var word = new GroupedWord(new LookupSpan([Token("一", "イチ"), Token("週間", "シュウカン", 1)],
            "一週間", [], "いっしゅうかん"), [firstLine, third],
            [[new(0, 0, 1, first)], [new(0, 0, 1, second), new(1, 1, 1, third)]]);

        var morae = PitchAccent.SplitMorae(word.Reading);
        var parts = FuriganaLayout.GetSpanParts(word, 0, 3, morae.Count);

        Assert.Equal([new FuriganaLinePart(firstLine, 0, 4), new FuriganaLinePart(third, 4, 2)], parts);
        Assert.Equal(["いっしゅう", "かん"],
            parts.Select(part => string.Concat(morae.Skip(part.MoraStart).Take(part.MoraCount))));
    }

    [Fact]
    public void Span_furigana_slices_a_partial_later_token_using_its_own_bounds()
    {
        var word = new GroupedWord(new LookupSpan([Token("頭", "アタマ"), Token("を", "ヲ", 1), Token("垂れ", "タレ", 2)],
            "頭を垂れる", [], "こうべをたれ"), [new ScreenRect(210, 160, 184, 36)],
            [[new(0, 0, 1, new ScreenRect(210, 160, 32, 36))],
             [new(0, 0, 1, new ScreenRect(260, 160, 30, 36))],
             [new(0, 0, 2, new ScreenRect(302, 161, 92, 35))]]);

        Assert.Equal([new FuriganaLinePart(new ScreenRect(302, 161, 46, 35), 0, 1)],
            FuriganaLayout.GetSpanParts(word, 2, 1, 1));
    }

    private static GroupedWord Word(string surface, string reading, ScreenRect first, ScreenRect second, int firstSurfaceLength)
        => new(new LookupSpan([Token(surface, reading)], surface, []), [first, second],
            [[new SourceTokenLineGeometry(0, 0, firstSurfaceLength, first),
              new SourceTokenLineGeometry(1, firstSurfaceLength, surface.Length - firstSurfaceLength, second)]]);

    private static Token Token(string surface, string reading, int start = 0)
        => new(surface, surface, surface, reading, reading, reading,
            new PartsOfSpeech("noun", "", "", ""), "", "", "1", start);
}

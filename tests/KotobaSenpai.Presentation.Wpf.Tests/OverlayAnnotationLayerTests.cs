using KotobaSenpai.Core.Japanese;
using System.Windows.Controls;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Presentation.Wpf.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Tests;

public sealed class OverlayAnnotationLayerTests
{
    [Fact]
    public async Task Appending_a_sentence_retains_existing_furigana_and_underlines_without_a_native_window()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var target = new WindowTarget((nint)42, "Target", new ScreenRect(100, 100, 700, 400));
                var layer = new OverlayAnnotationLayer();
                GroupedWord Word(string surface, string reading, int row) => new GroupedWord(new Token(
                    surface, surface, surface, reading, reading, reading, new("noun", "", "", ""), "", "", "1", 0),
                    new ScreenRect(180, 160 + row * 90, 90, 40)).WithSegmentId($"s{row}-{row}");
                layer.Render(WordOverlaySession.Start(target, [Word("僕", "ボク", 0)], underlineSegmentIds: ["s0-0"]), 1, 1d / 3);
                var canvas = layer.Canvas;
                var original = canvas.Children.OfType<OverlayAnnotation>().ToArray();
                Assert.Contains(original, annotation => annotation.Glyph is not null);
                Assert.Contains(original, annotation => annotation.Role == OverlayColorRole.Underline);

                layer.Render(WordOverlaySession.Start(target,
                    [Word("僕", "ボク", 0), Word("職員", "ショクイン", 1)], underlineSegmentIds: ["s0-0"]), 1, 1d / 3);

                Assert.All(original, annotation => Assert.Contains(annotation, canvas.Children.OfType<OverlayAnnotation>()));
                Assert.True(canvas.Children.Count > original.Length);
                var secondLine = canvas.Children.OfType<OverlayAnnotation>().Except(original).ToArray();
                layer.Render(WordOverlaySession.Start(target,
                    [Word("僕", "ボク", 0), Word("職員", "ショクイン", 1)], underlineSegmentIds: ["s0-0", "s1-1"]), 1, 1d / 3);
                Assert.All(original, annotation => Assert.Contains(annotation, canvas.Children.OfType<OverlayAnnotation>()));
                Assert.DoesNotContain(secondLine, annotation => canvas.Children.Contains(annotation));
                Assert.Equal([80d, 80d], canvas.Children.OfType<OverlayAnnotation>()
                    .Where(annotation => annotation.Role == OverlayColorRole.Underline).Select(Canvas.GetLeft));

                layer.Render(WordOverlaySession.Start(target, []), 1, 1d / 3);
                Assert.Empty(canvas.Children);
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    [Theory]
    [InlineData("頭", "アタマ", "垂れる", "タレル", "", "", "こうべをたれる", "こうべ", "た")]
    [InlineData("頭", "アタマ", "垂れ", "タレ", "た", "タ", "こうべをたれた", "こうべ", "た")]
    [InlineData("踵", "カカト", "返し", "カエシ", "て", "テ", "きびすをかえして", "きびす", "かえ")]
    public Task Overridden_phrase_draws_readings_above_kanji_without_pitch_markers(
        string first, string firstKana, string verb, string verbKana, string suffix, string suffixKana,
        string reading, string firstFurigana, string verbFurigana) => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var target = Target();
        List<Token> tokens = [Token(first, firstKana), Token("を", "ヲ", 1, "*"), Token(verb, verbKana, 2, "*")];
        List<IReadOnlyList<SourceTokenLineGeometry>> geometry =
        [
            [new(0, 0, 1, new ScreenRect(220, 160, 34, 36))],
            [new(0, 0, 1, new ScreenRect(260, 160, 29, 36))],
            [new(0, 0, verb.Length, new ScreenRect(300, 160, 50 * verb.Length, 36))],
        ];
        var width = 80 + 50 * verb.Length;
        if (suffix.Length > 0)
        {
            tokens.Add(Token(suffix, suffixKana, 2 + verb.Length, "*"));
            geometry.Add([new(0, 0, suffix.Length, new ScreenRect(220 + width, 160, 26, 36))]);
            width += 26;
        }
        var word = new GroupedWord(new LookupSpan(tokens, first + "を" + verb + suffix, [], reading),
            [new ScreenRect(220, 160, width, 36)], geometry);

        layer.Render(WordOverlaySession.Start(target, [word], underlineSegmentIds: []), 1, 1d / 3);

        var annotations = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        Assert.Equal(2, annotations.Length);
        AssertFurigana(annotations[0], firstFurigana, new ScreenRect(220, 160, 34, 36), target);
        AssertFurigana(annotations[1], verbFurigana, new ScreenRect(300, 160, 50, 36), target);
    });

    [Fact]
    public Task LexicalReferenceDoesNotColorAnOverriddenPhraseOrDrawStaleDots() => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var target = Target();
        var nounBounds = new ScreenRect(220, 160, 50, 36);
        var verbBounds = new ScreenRect(320, 160, 60, 36);
        var word = new GroupedWord(new LookupSpan(
            [Token("頭", "アタマ"), Token("を", "ヲ", 1, "*"), Token("垂れる", "タレル", 2, "2")],
            "頭を垂れる", []), [new ScreenRect(220, 160, 160, 36)],
            [[new(0, 0, 1, nounBounds)], [new(0, 0, 1, new ScreenRect(270, 160, 50, 36))],
             [new(0, 0, 3, verbBounds)]]);
        layer.Render(WordOverlaySession.Start(target, [word], underlineSegmentIds: []), 1, 1d / 3);
        Assert.Contains(layer.Canvas.Children.OfType<OverlayAnnotation>(), element =>
            element.Glyph is null && element.Role == OverlayColorRole.Low);

        var corrected = word.WithOverriddenReading("こうべをたれる").WithDictionaryWordPitch(new StubWordPitchLookup([0, 3]));
        Assert.NotNull(corrected.WordPitch);
        layer.Render(WordOverlaySession.Start(target, [corrected], underlineSegmentIds: []), 1, 1d / 3);

        var annotations = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        Assert.Equal([OverlayColorRole.Furigana, OverlayColorRole.Furigana], RenderedRoles(layer));
        AssertFurigana(annotations[0], "こうべ", nounBounds, target);
        AssertFurigana(annotations[1], "た", new ScreenRect(320, 160, 20, 36), target);
    });

    [Theory]
    [InlineData("一")]
    [InlineData("1")]
    public Task SpanPitchKeepsMoraOffsetsAcrossLinesAndUsesOnlyTheFirstCandidate(string number) => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var target = Target();
        var first = new ScreenRect(280, 160, 40, 36);
        var second = new ScreenRect(220, 220, 96, 36);
        var word = new GroupedWord(new LookupSpan([Token(number, "イチ"), Token("週間", "シュウカン", 1)],
            "一週間", [], "いっしゅうかん"), [first, second], [[new(0, 0, 1, first)], [new(1, 0, 2, second)]])
        {
            SpanPitch = new("いっしゅうかん", [3, 0], SpanPitchSource.Dictionary),
        };

        layer.Render(WordOverlaySession.Start(target, [word], underlineSegmentIds: []), 1, 1d / 3);

        var annotations = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        Assert.Equal([OverlayColorRole.Low, OverlayColorRole.High, OverlayColorRole.High,
            OverlayColorRole.Low, OverlayColorRole.Low, OverlayColorRole.Low], RenderedRoles(layer));
        Assert.All(annotations, element => Assert.NotNull(element.Glyph));
        Assert.All(annotations.Take(2), element => Assert.Equal(46, Canvas.GetTop(element)));
        Assert.All(annotations.Skip(2), element => Assert.Equal(106, Canvas.GetTop(element)));
    });

    [Theory]
    [InlineData("3", "か月", "さんかげつ", "さ", "ん")]
    [InlineData("1", "か月", "いっかげつ", "い", "っ")]
    [InlineData("3", "カ月", "さんかげつ", "さ", "ん")]
    [InlineData("３", "か月", "さんかげつ", "さ", "ん")]
    public Task SpanPitchRendersNumericCounterFuriganaAndKanaDot(
        string number, string counter, string reading, string firstMora, string secondMora) => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var word = new GroupedWord(new LookupSpan([Token(number, ""), Token(counter, "カゲツ", 1)],
            number + counter, [], reading), new ScreenRect(220, 160, 90, 36))
        {
            SpanPitch = new(reading, [3], SpanPitchSource.CounterRule),
        };

        layer.Render(WordOverlaySession.Start(Target(), [word], underlineSegmentIds: []), 1, 1d / 3);

        var annotations = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        Assert.Equal([OverlayColorRole.Low, OverlayColorRole.High, OverlayColorRole.High,
            OverlayColorRole.Low, OverlayColorRole.Low], RenderedRoles(layer));
        Assert.Null(annotations[2].Glyph);
        var style = new OverlayAnnotationStyle(OverlayAppearanceSettings.Default);
        var glyphs = new[] { firstMora, secondMora, "げ", "つ" }
            .Select(text => OverlayAnnotation.CreateText(text, 12, OverlayColorRole.Furigana, style).Glyph!.ToString());
        Assert.Equal(glyphs, annotations.Where(annotation => annotation.Glyph is not null)
            .Select(annotation => annotation.Glyph!.ToString()));
    });

    [Theory]
    [InlineData("3", "か月", "さんかげつ", "さん")]
    [InlineData("1", "か月", "いっかげつ", "いっ")]
    [InlineData("3", "カ月", "さんかげつ", "さん")]
    public Task MonochromeFuriganaIncludesTheNumericCounterPrefix(
        string number, string counter, string reading, string prefixReading) => RunOnSta(() =>
    {
        var target = Target();
        var layer = new OverlayAnnotationLayer();
        var word = new GroupedWord(new LookupSpan([Token(number, ""), Token(counter, "カゲツ", 1)],
            number + counter, [], reading), new ScreenRect(220, 160, 90, 36));

        layer.Render(WordOverlaySession.Start(target, [word], underlineSegmentIds: []), 1, 1d / 3);

        Assert.Collection(layer.Canvas.Children.OfType<OverlayAnnotation>(),
            prefix => AssertFurigana(prefix, prefixReading, new ScreenRect(220, 160, 30, 36), target),
            suffix => AssertFurigana(suffix, "げつ", new ScreenRect(280, 160, 30, 36), target));
    });

    [Theory]
    [InlineData("あした", "あした", 0)]
    [InlineData("受ける", "うける", 1)]
    public Task SpanPitchUsesDotsForKanaWithTheSameGlobalMoraOffsets(string surface, string reading, int textCount)
        => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var word = new GroupedWord(Token(surface, reading), new ScreenRect(220, 160, 90, 36))
        {
            SpanPitch = new(reading, [0, 3], SpanPitchSource.Dictionary),
        };

        layer.Render(WordOverlaySession.Start(Target(), [word], underlineSegmentIds: []), 1, 1d / 3);

        var annotations = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        Assert.Equal(textCount, annotations.Count(element => element.Glyph is not null));
        Assert.Equal([OverlayColorRole.Low, OverlayColorRole.High, OverlayColorRole.High, OverlayColorRole.Heiban],
            RenderedRoles(layer));
        Assert.All(annotations.Skip(textCount), element => Assert.Null(element.Glyph));
    });

    [Fact]
    public Task Ambiguous_correction_keeps_the_whole_reading_in_one_uncolored_annotation() => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var target = Target();
        var bounds = new ScreenRect(220, 160, 72, 36);
        var word = new GroupedWord(new LookupSpan([Token("阿", "ア"), Token("亜", "ア", 1)], "阿亜", [], "あああ"), bounds)
            .WithDictionaryWordPitch(new StubWordPitchLookup([0]));

        layer.Render(WordOverlaySession.Start(target, [word], underlineSegmentIds: []), 1, 1d / 3);

        AssertFurigana(Assert.Single(layer.Canvas.Children.OfType<OverlayAnnotation>()), "あああ", bounds, target);
    });

    private static OverlayColorRole[] RenderedRoles(OverlayAnnotationLayer layer)
        => layer.Canvas.Children.OfType<OverlayAnnotation>().Select(annotation => annotation.Role).ToArray();

    private sealed class StubWordPitchLookup(int[] positions) : IWordPitchLookup
    {
        public IReadOnlyList<int> FindWordPitch(Token source, string reading) => positions;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Overridden_kanji_run_uses_the_combined_token_bounds_on_each_line(bool wrapped) => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var target = Target();
        var first = new ScreenRect(280, 160, 34, 36);
        var second = wrapped ? new ScreenRect(220, 220, 94, 30) : new ScreenRect(320, 160, 94, 36);
        var combined = new ScreenRect(280, 160, 134, 36);
        var word = new GroupedWord(new LookupSpan([Token("一", "イチ"), Token("週間", "シュウカン", 1, "*")],
            "一週間", [], "いっしゅうかん"), wrapped ? [first, second] : [combined],
            [[new(0, 0, 1, first)], [new(wrapped ? 1 : 0, 0, 2, second)]]);

        layer.Render(WordOverlaySession.Start(target, [word], underlineSegmentIds: []), 1, 1d / 3);

        var annotations = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        if (wrapped)
        {
            Assert.Equal(2, annotations.Length);
            AssertFurigana(annotations[0], "いっ", first, target);
            AssertFurigana(annotations[1], "しゅうかん", second, target);
        }
        else
        {
            AssertFurigana(Assert.Single(annotations), "いっしゅうかん", combined, target);
        }
    });

    [Fact]
    public Task Refresh_replaces_only_annotations_for_the_word_whose_override_reading_or_underline_changed() => RunOnSta(() =>
    {
        var layer = new OverlayAnnotationLayer();
        var target = Target();
        var fixedWord = new GroupedWord(Token("僕", "ボク"), new ScreenRect(220, 160, 35, 36)).WithSegmentId("s0-0");
        var word = new GroupedWord(Token("額", "ガク"), new ScreenRect(220, 220, 39, 36)).WithSegmentId("s1-1");
        layer.Render(WordOverlaySession.Start(target, [fixedWord], underlineSegmentIds: ["s0-0"]), 1, 1d / 3);
        var fixedElements = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
        var original = Render(word, ["s0-0"]);
        Assert.Contains(original, element => element.Role == OverlayColorRole.Heiban);

        var overridden = word.WithOverriddenReading(word.Reading);
        var monochrome = Render(overridden, ["s0-0"], original);
        AssertFurigana(Assert.Single(monochrome), "がく", word.Bounds, target);
        Assert.Same(monochrome[0], Assert.Single(Render(overridden, ["s0-0"])));

        var corrected = overridden.WithOverriddenReading("ひたい");
        var changed = Render(corrected, ["s0-0"], monochrome);
        AssertFurigana(Assert.Single(changed), "ひたい", word.Bounds, target);

        var resolved = corrected with { SpanPitch = new("ひたい", [0], SpanPitchSource.Dictionary) };
        var pitch = Render(resolved, ["s0-0"], changed);
        Assert.Equal([OverlayColorRole.Low, OverlayColorRole.High, OverlayColorRole.High, OverlayColorRole.Heiban],
            pitch.Select(element => element.Role));
        var equivalent = resolved with { SpanPitch = new("ひたい", [0], SpanPitchSource.Dictionary) };
        Assert.Equal(pitch, Render(equivalent, ["s0-0"]));

        var underlined = Render(corrected, ["s0-0", "s1-1"], pitch);
        Assert.Contains(underlined, element => element.Role == OverlayColorRole.Underline);
        AssertFurigana(Assert.Single(underlined, element => element.Glyph is not null), "ひたい", word.Bounds, target);

        OverlayAnnotation[] Render(GroupedWord current, string[] underlinedSegments, OverlayAnnotation[]? old = null)
        {
            layer.Render(WordOverlaySession.Start(target, [fixedWord, current], underlineSegmentIds: underlinedSegments), 1, 1d / 3);
            var all = layer.Canvas.Children.OfType<OverlayAnnotation>().ToArray();
            Assert.All(fixedElements, element => Assert.Contains(element, all));
            if (old is not null)
                Assert.All(old, element => Assert.DoesNotContain(element, all));
            return all.Except(fixedElements).ToArray();
        }
    });

    private static void AssertFurigana(OverlayAnnotation annotation, string reading, ScreenRect bounds, WindowTarget target)
    {
        var fontSize = bounds.Height / 3d;
        var expected = OverlayAnnotation.CreateText(reading, fontSize, OverlayColorRole.Furigana,
            new OverlayAnnotationStyle(OverlayAppearanceSettings.Default));
        Assert.Equal(expected.Role, annotation.Role);
        Assert.Equal(expected.Glyph!.ToString(), annotation.Glyph?.ToString());
        Assert.Equal(bounds.X - target.Bounds.X + bounds.Width / 2d,
            Canvas.GetLeft(annotation) + annotation.DesiredSize.Width / 2d, precision: 6);
        Assert.Equal(bounds.Y - target.Bounds.Y - fontSize - 2, Canvas.GetTop(annotation), precision: 6);
    }

    private static WindowTarget Target() => new((nint)42, "Target", new ScreenRect(100, 100, 700, 400));

    private static Token Token(string surface, string reading, int start = 0, string aType = "0")
        => new(surface, surface, surface, reading, reading, reading, new("noun", "", "", ""), "", "", aType, start);

    private static Task RunOnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.PhraseAnalysis;

namespace KotobaSenpai.Core.Tests.WordGrouping;

public sealed class WordGroupingTests
{
    private static OcrLine Line(string text)
        => Line(text, 0);

    private static OcrLine Line(string text, int y)
        => new(text.Select((c, i) => new OcrWord(c.ToString(), new ScreenRect(10 * i, y, 10, 20))).ToArray());

    [Fact]
    public void Jmdic_span_merging_also_spans_lines()
    {
        // で on line0, も on line1; the span resolver merges them into でも (jmdic longest match) across the lines.
        var service = new WordGroupingService(
            new StubTokenizer(new TokenSpec("で", 0), new TokenSpec("も", 1)),
            new SentenceSegmenter(),
            new StubSpanResolver(new LookupSpan([Token("で", 0), Token("も", 1)], "でも", [])));
        var grouped = service.Group([Line("で", 0), Line("も", 30)]);

        var word = Assert.Single(grouped);
        Assert.Equal("でも", word.Surface);
        Assert.Equal(2, word.Rects.Count);
        Assert.Equal(0, word.Rects[0].Y);
        Assert.Equal(30, word.Rects[1].Y);
    }

    [Fact]
    public void Cross_line_word_becomes_one_grouped_word_with_two_rects()
    {
        // The merged block tokenizes あ+い into one token spanning both lines; the resolver carries it as one span => one word with one rect per line.
        var service = new WordGroupingService(
            new StubTokenizer(new TokenSpec("あ", 0), new TokenSpec("い", 1)),
            new SentenceSegmenter(),
            new StubSpanResolver(new LookupSpan([Token("あ", 0), Token("い", 1)], "あい", [])));
        var grouped = service.Group([Line("あ", 0), Line("い", 30)]);

        var word = Assert.Single(grouped);
        Assert.Equal("あい", word.Token.Surface);
        Assert.Equal(2, word.Rects.Count);
        Assert.Equal(0, word.Rects[0].Y);
        Assert.Equal(30, word.Rects[1].Y);
    }

    [Fact]
    public void Keeps_source_token_line_geometry_for_cross_line_span()
    {
        var service = new WordGroupingService(
            new StubTokenizer(new TokenSpec("困っ", 0), new TokenSpec("ちゃう", 2)),
            new SentenceSegmenter(),
            new StubSpanResolver(new LookupSpan(
                [Token("困っ", 0), Token("ちゃう", 2)],
                "困っちゃう",
                [])));

        var word = Assert.Single(service.Group([Line("困っ", 0), Line("ちゃう", 30)]));

        Assert.Equal([new ScreenRect(0, 0, 20, 20)], word.GetSourceTokenBounds(0));
        Assert.Equal([new ScreenRect(0, 30, 30, 20)], word.GetSourceTokenBounds(1));
        Assert.Equal(50, word.Bounds.Height);
    }

    [Fact]
    public void Keeps_surface_offsets_when_one_token_crosses_lines()
    {
        var service = new WordGroupingService(
            new StubTokenizer(new TokenSpec("あい", 0)),
            new SentenceSegmenter(),
            new StubSpanResolver(new LookupSpan([Token("あい", 0)], "あい", [])));

        var word = Assert.Single(service.Group([Line("あ", 0), Line("い", 30)]));

        Assert.Equal(
            [
                new SourceTokenLineGeometry(0, 0, 1, new ScreenRect(0, 0, 10, 20)),
                new SourceTokenLineGeometry(1, 1, 1, new ScreenRect(0, 30, 10, 20)),
            ],
            word.SourceTokenLines[0]);
    }

    [Fact]
    public void Reports_empty_for_no_lines()
    {
        var service = new WordGroupingService(
            new StubTokenizer(),
            new SentenceSegmenter(),
            new StubSpanResolver());
        Assert.Empty(service.Group([]));
    }

    [Fact]
    public void Uses_resolved_span_for_geometry_and_dictionary_entries()
    {
        var entry = new DictionaryEntry("でも", "でも", []);
        var resolver = new StubSpanResolver(new LookupSpan(
            [
                Token("で", 0),
                Token("も", 1),
            ],
            "でも",
            [entry]));
        var service = new WordGroupingService(
            new StubTokenizer(new TokenSpec("で", 0), new TokenSpec("も", 1)),
            new SentenceSegmenter(),
            resolver);

        var grouped = service.Group([Line("でも")]);

        var word = Assert.Single(grouped);
        Assert.Equal("でも", word.Surface);
        Assert.Equal(20, word.Bounds.Width);
        Assert.Same(entry, Assert.Single(word.Entries));
        Assert.True(word.HasResolvedLookup);
    }

    [Fact]
    public void Carries_sentence_segment_id_on_grouped_words()
    {
        var service = new WordGroupingService(
            new StubTokenizer(new TokenSpec("彼", 0)),
            new SentenceSegmenter(),
            new StubSpanResolver(new LookupSpan([Token("彼", 0)], "彼", [])));

        var grouped = service.Group([Line("彼")]);

        Assert.Equal("s0-0", Assert.Single(grouped).SegmentId);
    }

    [Fact]
    public void Grouped_words_and_local_summaries_share_span_order_and_effective_reading_in_each_segment()
    {
        var tokenizer = new StubTokenizer(new TokenSpec("頭", 0, "0", "アタマ"),
            new TokenSpec("を", 1, "0", "ヲ"), new TokenSpec("垂れる", 2, "2", "タレル"),
            new TokenSpec("彼", 5, "1", "カレ"));
        var tokens = tokenizer.Tokenize("頭を垂れる彼");
        var entry = new DictionaryEntry("頭を垂れる", "こうべをたれる", []);
        var resolver = new StubSpanResolver(new LookupSpan(tokens.Take(3).ToArray(), entry.Headword, [entry], entry.Reading),
            new LookupSpan([tokens[3]], "彼", []));
        OcrLine[] lines = [Line("頭を垂れる彼"), Line("頭を垂れる彼", 30)];
        SentenceSegment[] segments = [new([0]), new([1])];
        var service = new WordGroupingService(tokenizer, new SentenceSegmenter(), resolver);
        var builder = new SentenceTokenBuilder(tokenizer, resolver);

        var words = service.Group(lines, segments);

        Assert.Equal(4, words.Count);
        foreach (var segment in segments)
        {
            var summaries = builder.Build(lines, segment).LocalSpans;
            var segmentWords = words.Where(word => word.SegmentId == segment.SegmentId).ToArray();
            Assert.Equal([0, 1], summaries.Select(span => span.SpanIndex));
            Assert.Equal(summaries.Select(span => (span.SpanIndex, span.Surface, span.Reading)),
                segmentWords.Select(word => (word.SegmentSpanIndex, word.Surface, word.Reading)));
            Assert.Equal("こうべをたれる", segmentWords[0].Reading);
            Assert.True(segmentWords[0].HasOverriddenReading);
            Assert.Empty(segmentWords[0].PitchAccents);
            Assert.Empty(summaries[0].PitchAccents);
            Assert.False(segmentWords[1].HasOverriddenReading);
            Assert.Equal(1, Assert.Single(segmentWords[1].PitchAccents).AccentPosition);
        }

        var original = words[0];
        var updated = original.WithOverriddenReading("あたまをたれる");
        Assert.Equal("こうべをたれる", original.Reading);
        Assert.Equal("あたまをたれる", updated.Reading);
        Assert.True(updated.HasOverriddenReading);
        Assert.Empty(updated.PitchAccents);
        Assert.Null(updated.Token.PitchAccentPosition);
        Assert.Same(original.SourceTokens, updated.SourceTokens);
        Assert.Same(original.SourceTokenLines, updated.SourceTokenLines);
        Assert.Same(original.Rects, updated.Rects);
        Assert.Same(original.Entries, updated.Entries);
        Assert.Equal(original.LookupKey, updated.LookupKey);
        Assert.Equal(original.Bounds, updated.Bounds);
        Assert.True(updated.HasResolvedLookup);

        var copied = updated.WithRects(updated.Rects, updated.SourceTokenLines).WithSegmentId("moved");
        Assert.Equal(original.SegmentId, updated.SegmentId);
        Assert.Equal("moved", copied.SegmentId);
        Assert.Equal(0, copied.SegmentSpanIndex);
        Assert.Equal(updated.Reading, copied.Reading);
        Assert.True(copied.HasOverriddenReading);
        Assert.Empty(copied.PitchAccents);
    }

    private static Token Token(string surface, int start)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", start);
}

internal sealed record TokenSpec(string Surface, int StartOffset, string AType = "", string? Reading = null);

/// <summary>Splits on explicit spans so grouping tests can control the tokenization result precisely.</summary>
internal sealed class StubTokenizer : ITokenizer
{
    private readonly TokenSpec[] _specs;
    public StubTokenizer(params TokenSpec[] specs) => _specs = specs;

    public IReadOnlyList<Token> Tokenize(string? text)
        => _specs.Select(s => new Token(
            s.Surface, s.Surface, s.Surface, s.Reading ?? s.Surface, s.Reading ?? s.Surface, s.Reading ?? s.Surface,
            new PartsOfSpeech("", "", "", ""), "", "", s.AType, s.StartOffset)).ToArray();
}

internal sealed class StubSpanResolver : ITokenSpanResolver
{
    private readonly IReadOnlyList<LookupSpan> _spans;

    public StubSpanResolver(params LookupSpan[] spans) => _spans = spans;

    public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens) => _spans;
}

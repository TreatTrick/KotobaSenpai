using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

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

    private static Token Token(string surface, int start)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", start);
}

internal sealed record TokenSpec(string Surface, int StartOffset);

/// <summary>Splits on explicit spans so grouping tests can control the tokenization result precisely.</summary>
internal sealed class StubTokenizer : ITokenizer
{
    private readonly TokenSpec[] _specs;
    public StubTokenizer(params TokenSpec[] specs) => _specs = specs;

    public IReadOnlyList<Token> Tokenize(string? text)
        => _specs.Select(s => new Token(
            s.Surface, s.Surface, s.Surface, s.Surface, s.Surface, s.Surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", s.StartOffset)).ToArray();
}

internal sealed class StubSpanResolver : ITokenSpanResolver
{
    private readonly IReadOnlyList<LookupSpan> _spans;

    public StubSpanResolver(params LookupSpan[] spans) => _spans = spans;

    public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens) => _spans;
}
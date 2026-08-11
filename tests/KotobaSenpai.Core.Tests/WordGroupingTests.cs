using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

public sealed class WordGroupingTests
{
    private static OcrLine Line(string text)
        => new(text.Select((c, i) => new OcrWord(c.ToString(), new ScreenRect(10 * i, 0, 10, 20))).ToArray());

    [Fact]
    public void Groups_multi_character_word_into_single_wide_line()
    {
        var service = new WordGroupingService(new StubTokenizer(new TokenSpec("日本語", 0)));
        var grouped = service.Group([Line("日本語")]);

        Assert.Single(grouped);
        Assert.Equal("日本語", grouped[0].Token.Surface);
        Assert.Equal(30, grouped[0].Bounds.Width); // 三个字符框并集 = 30 宽
        Assert.Equal(20, grouped[0].Bounds.Height);
    }

    [Fact]
    public void Emits_separate_line_per_word_including_particles()
    {
        var service = new WordGroupingService(new StubTokenizer(
            new TokenSpec("彼", 0), new TokenSpec("が", 1)));
        var grouped = service.Group([Line("彼が")]);

        Assert.Equal(2, grouped.Count);
        Assert.Equal("彼", grouped[0].Token.Surface);
        Assert.Equal(10, grouped[0].Bounds.Width);
        Assert.Equal("が", grouped[1].Token.Surface); // 助词被保留
        Assert.Equal(10, grouped[1].Bounds.Width);
    }

    [Fact]
    public void Skips_punctuation_tokens()
    {
        var service = new WordGroupingService(new StubTokenizer(
            new TokenSpec("彼", 0), new TokenSpec("、", 1), new TokenSpec("が", 2)));
        var grouped = service.Group([Line("彼、が")]);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(new[] { "彼", "が" }, grouped.Select(g => g.Token.Surface).ToArray());
    }

    [Fact]
    public void Skips_token_without_member_characters()
    {
        var service = new WordGroupingService(new StubTokenizer(
            new TokenSpec("日", 0), new TokenSpec("X", 5)));
        var grouped = service.Group([Line("日")]);

        Assert.Single(grouped);
        Assert.Equal("日", grouped[0].Token.Surface);
    }

    [Fact]
    public void Reports_empty_for_no_lines()
    {
        var service = new WordGroupingService(new StubTokenizer());
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

/// <summary>按显式 span 切分，供分组测试精确控制分词结果。</summary>
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

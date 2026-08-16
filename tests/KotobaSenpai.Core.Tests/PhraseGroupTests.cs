using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

public sealed class PhraseSegmentationTests
{
    private static OcrLine Line(string text, int y = 0, int xStart = 0)
        => new(text.Select((c, i) => new OcrWord(c.ToString(), new ScreenRect(xStart + 10 * i, y, 10, 20))).ToArray());

    [Fact]
    public void Joins_adjacent_lines_without_boundary()
    {
        var segments = new SentenceSegmenter().Segment([Line("こんにちは", 0), Line("世界", 0)]);
        Assert.Single(segments);
        Assert.Equal([0, 1], segments[0].LineIndices);
    }

    [Fact]
    public void Breaks_on_sentence_final_punctuation()
    {
        var segments = new SentenceSegmenter().Segment([Line("こんにちは。", 0), Line("世界", 0)]);
        Assert.Equal(2, segments.Count);
        Assert.Equal([0], segments[0].LineIndices);
        Assert.Equal([1], segments[1].LineIndices);
    }

    [Fact]
    public void Breaks_on_large_vertical_gap()
    {
        var segments = new SentenceSegmenter().Segment([Line("上", 0), Line("下", 100)]);
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void Reading_order_reversal_does_not_break_a_wrapped_sentence()
    {
        // A wrapped line may start further left; without a gap or punctuation it is still the same sentence, so it merges.
        var segments = new SentenceSegmenter().Segment([Line("右", 0, xStart: 50), Line("左", 0, xStart: 0)]);
        Assert.Single(segments);
        Assert.Equal([0, 1], segments[0].LineIndices);
    }

    [Fact]
    public void Embedded_closing_quote_does_not_break_a_sentence()
    {
        // 」 alone is not sentence-final; only 。！？… break.
        var segments = new SentenceSegmenter().Segment([Line("「彼は「行こう」と", 0), Line("言ったよ。」", 0)]);
        Assert.Single(segments);
    }
}

public sealed class SentenceTokenBuilderTests
{
    private static OcrLine Line(string text, int y = 0)
        => new(text.Select((c, i) => new OcrWord(c.ToString(), new ScreenRect(i * 10, y, 10, 20))).ToArray());

    [Fact]
    public void Builds_cross_line_references_with_sentence_index_order()
    {
        var builder = new SentenceTokenBuilder(new CharTokenizer());
        var built = builder.Build([Line("あ"), Line("い")], new SentenceSegment([0, 1]));

        Assert.Equal(2, built.References.Count);
        Assert.Equal("l0:t0", built.References[0].Id.ToString());
        Assert.Equal(0, built.References[0].SentenceIndex);
        // Merged-block tokenization: the second token's line id is its first line (1), token index is sequential (1).
        Assert.Equal("l1:t1", built.References[1].Id.ToString());
        Assert.Equal(1, built.References[1].SentenceIndex);
    }

    [Fact]
    public void Skips_token_without_member_characters()
    {
        var builder = new SentenceTokenBuilder(new StubTokenizer(
            new TokenSpec("あ", 0), new TokenSpec("X", 5)));
        var built = builder.Build([Line("あ")], new SentenceSegment([0]));
        Assert.Single(built.References);
    }

    [Fact]
    public void Builds_local_span_summaries_from_resolver()
    {
        var builder = new SentenceTokenBuilder(
            new StubTokenizer(new TokenSpec("で", 0), new TokenSpec("も", 1)),
            new StubSpanResolver(new LookupSpan([Token("で", 0), Token("も", 1)], "でも", [])));
        var built = builder.Build([Line("でも")], new SentenceSegment([0]));

        var span = Assert.Single(built.LocalSpans);
        Assert.Equal("でも", span.Surface);
        Assert.Equal([SentenceTokenId.Parse("l0:t0"), SentenceTokenId.Parse("l0:t1")], span.TokenIds);
    }

    private static Token Token(string surface, int start)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", start);
}

public sealed class PhraseGeometryTests
{
    [Fact]
    public void Maps_same_line_part_to_single_union_rect()
    {
        var part = Part(
            Ref("l0:t0", 0, 0, new ScreenRect(0, 0, 10, 20)),
            Ref("l0:t1", 1, 0, new ScreenRect(10, 0, 10, 20)));
        var rects = PhraseGeometryMapper.MapPart(part).Rects;
        Assert.Single(rects);
        var rect = rects[0];
        Assert.Equal(new ScreenRect(0, 0, 20, 20), rect);
    }

    [Fact]
    public void Maps_cross_line_part_to_per_line_rects()
    {
        var part = Part(
            Ref("l0:t0", 0, 0, new ScreenRect(0, 0, 10, 20)),
            Ref("l1:t0", 1, 1, new ScreenRect(0, 100, 10, 20)));
        var rects = PhraseGeometryMapper.MapPart(part).Rects;
        Assert.Equal(2, rects.Count);
        Assert.Equal(new ScreenRect(0, 0, 10, 20), rects[0]);
        Assert.Equal(new ScreenRect(0, 100, 10, 20), rects[1]);
    }

    [Fact]
    public void Maps_separated_parts_to_two_rects()
    {
        var group = new PhraseGroup("g1", "grammar",
        [
            Part(Ref("l0:t0", 0, 0, new ScreenRect(0, 0, 10, 20))),
            Part(Ref("l0:t2", 2, 0, new ScreenRect(20, 0, 10, 20))),
        ], "label", "意思", "语法", Guid.NewGuid());
        var view = PhraseGeometryMapper.MapGroup(group);
        Assert.Equal(2, view.Parts.Count);
        Assert.Single(view.Parts[0].Rects);
        Assert.Single(view.Parts[1].Rects);
    }

    private static PhraseGroupPart Part(params SentenceTokenReference[] tokens) => new(tokens);

    private static SentenceTokenReference Ref(string id, int sentenceIndex, int line, ScreenRect box)
        => new(sentenceIndex, line, id[^1] - '0', 0, Token(), [box]);

    private static Token Token()
        => new("あ", "あ", "あ", "あ", "あ", "あ",
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);
}

public sealed class PhraseGroupValidatorTests
{
    private static readonly SentenceTokenReference[] Refs =
    [
        Ref("l0:t0", 0),
        Ref("l0:t1", 1),
        Ref("l0:t2", 2),
        Ref("l0:t3", 3),
    ];

    private static IReadOnlyDictionary<SentenceTokenId, SentenceTokenReference> Map()
        => Refs.ToDictionary(r => r.Id);

    [Fact]
    public void Accepts_valid_multi_part_group()
    {
        var result = PhraseGroupValidator.ValidateAndBuild(
            [Group("g1", [["l0:t0"], ["l0:t2"]])], Map());
        var group = Assert.Single(result.ValidGroups);
        Assert.Equal("g1", group.ModelGroupId);
        Assert.Equal(2, group.Parts.Count);
        Assert.Equal(0, result.DroppedCount);
    }

    [Fact]
    public void Drops_group_with_unknown_token()
    {
        var result = PhraseGroupValidator.ValidateAndBuild(
            [Group("g1", [["l0:t0"], ["l9:t9"]])], Map());
        Assert.Empty(result.ValidGroups);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Drops_group_with_non_contiguous_part()
    {
        var result = PhraseGroupValidator.ValidateAndBuild(
            [Group("g1", [["l0:t0", "l0:t2"]])], Map());
        Assert.Empty(result.ValidGroups);
    }

    [Fact]
    public void Drops_group_that_repeats_a_token()
    {
        var result = PhraseGroupValidator.ValidateAndBuild(
            [Group("g1", [["l0:t0"], ["l0:t0"]])], Map());
        Assert.Empty(result.ValidGroups);
    }

    [Fact]
    public void Retains_valid_group_alongside_malformed_one()
    {
        var result = PhraseGroupValidator.ValidateAndBuild(
            [Group("g1", [["l9:t9"]]), Group("g2", [["l0:t0"]])], Map());
        var group = Assert.Single(result.ValidGroups);
        Assert.Equal("g2", group.ModelGroupId);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Caps_at_eight_groups_in_provider_order()
    {
        var groups = Enumerable.Range(0, 10)
            .Select(i => Group($"g{i}", [[$"l0:t{i % 2}"]]))
            .ToArray();
        var result = PhraseGroupValidator.ValidateAndBuild(groups, Map());
        Assert.Equal(8, result.ValidGroups.Count);
        Assert.Equal(2, result.DroppedCount);
    }

    [Fact]
    public void Drops_oversized_label()
    {
        var result = PhraseGroupValidator.ValidateAndBuild(
            [Group("g1", [["l0:t0"]], label: new string('x', PhraseGroup.MaxLabelLength + 1))], Map());
        Assert.Empty(result.ValidGroups);
    }

    private static ParsedPhraseGroup Group(string id, IReadOnlyList<IReadOnlyList<string>> parts, string? label = null)
        => new(id, "grammar",
            parts.Select(part => (IReadOnlyList<SentenceTokenId>)part.Select(SentenceTokenId.Parse).ToArray()).ToArray(),
            label ?? "标签", "中文意思", "中文语法解释");

    private static SentenceTokenReference Ref(string id, int sentenceIndex)
    {
        var (line, token) = (id[1] - '0', id[4] - '0');
        return new SentenceTokenReference(sentenceIndex, line, token, token, Token(), [new ScreenRect(0, 0, 10, 20)]);
    }

    private static Token Token()
        => new("あ", "あ", "あ", "あ", "あ", "あ",
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);
}

/// <summary>Tokenizes the input text character by character so sentence-building tests can control each token precisely.</summary>
internal sealed class CharTokenizer : ITokenizer
{
    public IReadOnlyList<Token> Tokenize(string? text)
        => (text ?? string.Empty).Select((c, i) => new Token(
            c.ToString(), c.ToString(), c.ToString(), c.ToString(), c.ToString(), c.ToString(),
            new PartsOfSpeech("", "", "", ""), "", "", "", i)).ToArray();
}
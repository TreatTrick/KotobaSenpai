using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Geometry;
namespace KotobaSenpai.Core.WordGrouping;

/// <summary>Geometry for one contiguous source-token range on one OCR line.</summary>
public sealed record SourceTokenLineGeometry(
    int LineIndex,
    int SurfaceOffset,
    int SurfaceLength,
    ScreenRect Bounds);

/// <summary>A grouped word produced by mapping OCR characters onto a token (or merged span). A word spanning multiple lines carries one rect per line.</summary>
public sealed record GroupedWord
{
    public GroupedWord(Token token, IReadOnlyList<ScreenRect> rects)
    {
        Token = token ?? throw new ArgumentNullException(nameof(token));
        Rects = rects.ToArray();
        if (Rects.Count == 0)
            throw new ArgumentException("A grouped word must have at least one rect.", nameof(rects));
        Bounds = Union(Rects);
        SourceTokens = [Token];
        LookupKey = Token.Lemma;
        Entries = Array.Empty<DictionaryEntry>();
        HasResolvedLookup = false;
        SourceTokenLines = CreateFallbackSourceTokenLines([Token], Rects, Bounds);
    }

    public GroupedWord(Token token, ScreenRect bounds)
        : this(token, new[] { bounds })
    {
    }

    public GroupedWord(LookupSpan span, IReadOnlyList<ScreenRect> rects)
        : this(span, rects, null)
    {
    }

    public GroupedWord(
        LookupSpan span,
        IReadOnlyList<ScreenRect> rects,
        IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>>? sourceTokenLines)
        : this((span ?? throw new ArgumentNullException(nameof(span))).Token, rects)
    {
        SourceTokens = span.Tokens;
        LookupKey = span.LookupKey;
        Entries = span.Entries;
        HasResolvedLookup = true;
        SourceTokenLines = sourceTokenLines is null
            ? CreateFallbackSourceTokenLines(SourceTokens, Rects, Bounds)
            : ValidateSourceTokenLines(sourceTokenLines, SourceTokens.Count);
    }

    public GroupedWord(LookupSpan span, ScreenRect bounds)
        : this(span, new[] { bounds })
    {
    }

    /// <summary>The original UniDic tokens that make up the merged word group; defaults to the current token when constructed by legacy callers.</summary>
    public IReadOnlyList<Token> SourceTokens { get; }

    public Token Token { get; }

    /// <summary>One rect per line the word spans; a single-line word has exactly one.</summary>
    public IReadOnlyList<ScreenRect> Rects { get; }

    /// <summary>Per-source-token line ranges retained for rendering annotations on wrapped words.</summary>
    public IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>> SourceTokenLines { get; }

    /// <summary>The sentence segment that produced this word, when available.</summary>
    public string? SegmentId { get; }

    /// <summary>The union of <see cref="Rects"/>, for consumers that need a single box (hover hit-test, covered-word detection).</summary>
    public ScreenRect Bounds { get; }

    public string Surface => Token.Surface;

    public string Reading => Token.Reading;

    public string LookupKey { get; }

    public IReadOnlyList<DictionaryEntry> Entries { get; }

    /// <summary>Distinguishes "pre-resolved but not matched" from legacy callers that haven't performed pre-resolution.</summary>
    public bool HasResolvedLookup { get; }

    /// <summary>Pitch data for the original source tokens, in merged-word surface order.</summary>
    public IReadOnlyList<PitchAccentSummary> PitchAccents
        => BuildPitchAccents(SourceTokens);

    /// <summary>Returns the screen bounds for one source token, split by OCR line.</summary>
    public IReadOnlyList<ScreenRect> GetSourceTokenBounds(int sourceTokenIndex)
    {
        if (sourceTokenIndex < 0 || sourceTokenIndex >= SourceTokenLines.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceTokenIndex));
        return SourceTokenLines[sourceTokenIndex].Select(line => line.Bounds).ToArray();
    }

    /// <summary>Replaces rects and, when supplied, their source-token line geometry; otherwise rebuilds a safe fallback.</summary>
    public GroupedWord WithRects(
        IReadOnlyList<ScreenRect> rects,
        IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>>? sourceTokenLines = null)
        => new(Token, rects, SourceTokens, LookupKey, Entries, HasResolvedLookup, SegmentId,
            sourceTokenLines ?? CreateFallbackSourceTokenLines(SourceTokens, rects, Union(rects)));

    /// <summary>Associates the word with its sentence segment for staged overlay publication.</summary>
    public GroupedWord WithSegmentId(string? segmentId)
        => new(Token, Rects, SourceTokens, LookupKey, Entries, HasResolvedLookup, segmentId, SourceTokenLines);

    private GroupedWord(
        Token token,
        IReadOnlyList<ScreenRect> rects,
        IReadOnlyList<Token> sourceTokens,
        string lookupKey,
        IReadOnlyList<DictionaryEntry> entries,
        bool hasResolvedLookup,
        string? segmentId,
        IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>> sourceTokenLines)
        : this(token, rects)
    {
        SourceTokens = sourceTokens;
        LookupKey = lookupKey;
        Entries = entries;
        HasResolvedLookup = hasResolvedLookup;
        SegmentId = segmentId;
        SourceTokenLines = ValidateSourceTokenLines(sourceTokenLines, SourceTokens.Count);
    }

    /// <summary>The added pre-resolution metadata does not change the token+union-bounds value-equality semantics.</summary>
    public bool Equals(GroupedWord? other)
        => ReferenceEquals(this, other)
            || (other is not null && Token == other.Token && Bounds == other.Bounds);

    public override int GetHashCode() => HashCode.Combine(Token, Bounds);

    private static IReadOnlyList<PitchAccentSummary> BuildPitchAccents(IReadOnlyList<Token> tokens)
    {
        var offset = 0;
        var result = new List<PitchAccentSummary>(tokens.Count);
        foreach (var token in tokens)
        {
            result.Add(PitchAccentSummary.FromToken(token, offset));
            offset += token.Surface.Length;
        }
        return result;
    }

    private static ScreenRect Union(IReadOnlyList<ScreenRect> rects)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
        foreach (var r in rects)
        {
            x1 = Math.Min(x1, r.X);
            y1 = Math.Min(y1, r.Y);
            x2 = Math.Max(x2, r.Right);
            y2 = Math.Max(y2, r.Bottom);
        }
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }

    private static IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>> ValidateSourceTokenLines(
        IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>> sourceTokenLines,
        int tokenCount)
    {
        ArgumentNullException.ThrowIfNull(sourceTokenLines);
        if (sourceTokenLines.Count != tokenCount)
            throw new ArgumentException("Source-token line geometry must align with source tokens.", nameof(sourceTokenLines));
        return sourceTokenLines.Select(lines => (IReadOnlyList<SourceTokenLineGeometry>)lines.ToArray()).ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<SourceTokenLineGeometry>> CreateFallbackSourceTokenLines(
        IReadOnlyList<Token> tokens,
        IReadOnlyList<ScreenRect> rects,
        ScreenRect bounds)
    {
        var result = new List<IReadOnlyList<SourceTokenLineGeometry>>(tokens.Count);
        if (tokens.Count == rects.Count)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                result.Add([
                    new SourceTokenLineGeometry(i, 0, tokens[i].Surface.Length, rects[i])]);
            }
            return result;
        }

        var totalLength = Math.Max(1, tokens.Sum(token => token.Surface.Length));
        var offset = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var length = tokens[i].Surface.Length;
            var left = bounds.X + bounds.Width * offset / totalLength;
            var right = bounds.X + bounds.Width * (offset + length) / totalLength;
            result.Add([
                new SourceTokenLineGeometry(
                    0,
                    0,
                    length,
                    new ScreenRect(left, bounds.Y, Math.Max(1, right - left), bounds.Height))]);
            offset += length;
        }
        return result;
    }
}

namespace KotobaSenpai.Core.Models;

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
    }

    public GroupedWord(Token token, ScreenRect bounds)
        : this(token, new[] { bounds })
    {
    }

    public GroupedWord(LookupSpan span, IReadOnlyList<ScreenRect> rects)
        : this((span ?? throw new ArgumentNullException(nameof(span))).Token, rects)
    {
        SourceTokens = span.Tokens;
        LookupKey = span.LookupKey;
        Entries = span.Entries;
        HasResolvedLookup = true;
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

    /// <summary>The union of <see cref="Rects"/>, for consumers that need a single box (hover hit-test, covered-word detection).</summary>
    public ScreenRect Bounds { get; }

    public string Surface => Token.Surface;

    public string Reading => Token.Reading;

    public string LookupKey { get; }

    public IReadOnlyList<DictionaryEntry> Entries { get; }

    /// <summary>Distinguishes "pre-resolved but not matched" from legacy callers that haven't performed pre-resolution.</summary>
    public bool HasResolvedLookup { get; }

    /// <summary>Replaces only the rects (e.g. frame -&gt; screen), reusing the already-resolved token/span/entries references.</summary>
    public GroupedWord WithRects(IReadOnlyList<ScreenRect> rects)
        => new(Token, rects, SourceTokens, LookupKey, Entries, HasResolvedLookup);

    private GroupedWord(
        Token token,
        IReadOnlyList<ScreenRect> rects,
        IReadOnlyList<Token> sourceTokens,
        string lookupKey,
        IReadOnlyList<DictionaryEntry> entries,
        bool hasResolvedLookup)
        : this(token, rects)
    {
        SourceTokens = sourceTokens;
        LookupKey = lookupKey;
        Entries = entries;
        HasResolvedLookup = hasResolvedLookup;
    }

    /// <summary>The added pre-resolution metadata does not change the token+union-bounds value-equality semantics.</summary>
    public bool Equals(GroupedWord? other)
        => ReferenceEquals(this, other)
            || (other is not null && Token == other.Token && Bounds == other.Bounds);

    public override int GetHashCode() => HashCode.Combine(Token, Bounds);

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
}
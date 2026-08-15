namespace KotobaSenpai.Core.Models;

/// <summary>A screen word group produced by mapping OCR characters onto the final lookup span.</summary>
public sealed record GroupedWord(Token Token, ScreenRect Bounds)
{
    public GroupedWord(LookupSpan span, ScreenRect bounds)
        : this(
            span?.Token ?? throw new ArgumentNullException(nameof(span)),
            bounds,
            span.Tokens,
            span.LookupKey,
            span.Entries,
            hasResolvedLookup: true)
    {
    }

    private GroupedWord(
        Token token,
        ScreenRect bounds,
        IReadOnlyList<Token> sourceTokens,
        string lookupKey,
        IReadOnlyList<DictionaryEntry> entries,
        bool hasResolvedLookup)
        : this(token, bounds)
    {
        SourceTokens = sourceTokens;
        LookupKey = lookupKey;
        Entries = entries;
        HasResolvedLookup = hasResolvedLookup;
    }

    /// <summary>The original UniDic tokens that make up the merged word group; defaults to the current token when constructed by legacy callers.</summary>
    public IReadOnlyList<Token> SourceTokens { get; } = [Token];

    public string Surface => Token.Surface;

    public string Reading => Token.Reading;

    public string LookupKey { get; } = Token.Lemma;

    public IReadOnlyList<DictionaryEntry> Entries { get; } = Array.Empty<DictionaryEntry>();

    /// <summary>Distinguishes "pre-resolved but not matched" from legacy callers that haven't performed pre-resolution.</summary>
    public bool HasResolvedLookup { get; }

    /// <summary>Replaces only the coordinates, reusing the already-resolved token/span/entries references.</summary>
    public GroupedWord WithBounds(ScreenRect bounds)
        => new(Token, bounds, SourceTokens, LookupKey, Entries, HasResolvedLookup);

    /// <summary>The added pre-resolution metadata does not change the original positional record's value-equality semantics.</summary>
    public bool Equals(GroupedWord? other)
        => ReferenceEquals(this, other)
            || (other is not null && Token == other.Token && Bounds == other.Bounds);

    public override int GetHashCode() => HashCode.Combine(Token, Bounds);
}

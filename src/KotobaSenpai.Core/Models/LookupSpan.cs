namespace KotobaSenpai.Core.Models;

/// <summary>
/// A lookupable span constrained by UniDic token boundaries.
/// <para>
/// <see cref="Tokens"/> retains the original morphemes; <see cref="Token"/> is a merged surface view for the
/// existing UI/diagnostic interfaces. Dictionary results are attached to the span during recognition so that
/// hovering doesn't have to re-guess from individual characters.
/// </para>
/// </summary>
public sealed record LookupSpan
{
    public LookupSpan(
        IReadOnlyList<Token> tokens,
        string lookupKey,
        IReadOnlyList<DictionaryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(lookupKey);
        ArgumentNullException.ThrowIfNull(entries);
        if (tokens.Count == 0)
            throw new ArgumentException("A lookup span must contain at least one token.", nameof(tokens));

        Tokens = tokens.ToArray();
        LookupKey = lookupKey;
        Entries = entries.ToArray();
        Surface = string.Concat(Tokens.Select(token => token.Surface));
        Reading = string.Concat(Tokens.Select(token => token.Reading));
        StartOffset = Tokens[0].StartOffset;
        EndOffset = Tokens[^1].StartOffset + Tokens[^1].Surface.Length;
        Token = CreateDisplayToken(Tokens, lookupKey, Surface, Reading);
        PitchAccents = BuildPitchAccents(Tokens);
    }

    /// <summary>The original UniDic tokens composing this span, in input order.</summary>
    public IReadOnlyList<Token> Tokens { get; }

    /// <summary>Merged token view for compatibility with existing popup/diagnostic APIs.</summary>
    public Token Token { get; }

    /// <summary>The actual OCR text covered.</summary>
    public string Surface { get; }

    /// <summary>The concatenated occurring readings of the constituent tokens.</summary>
    public string Reading { get; }

    /// <summary>The key used for the dictionary hit (the direct surface or the base token lemma).</summary>
    public string LookupKey { get; }

    /// <summary>UTF-16 start/end offsets in the input string, with the range [StartOffset, EndOffset).</summary>
    public int StartOffset { get; }

    public int EndOffset { get; }

    /// <summary>The dictionary results this span obtained during recognition; empty when nothing matched.</summary>
    public IReadOnlyList<DictionaryEntry> Entries { get; }

    /// <summary>Pitch data aligned with each source token, in source order.</summary>
    public IReadOnlyList<PitchAccentSummary> PitchAccents { get; }

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

    private static Token CreateDisplayToken(
        IReadOnlyList<Token> tokens,
        string lookupKey,
        string surface,
        string reading)
    {
        var first = tokens[0];
        return new Token(
            surface,
            lookupKey,
            lookupKey,
            reading,
            string.Concat(tokens.Select(token => token.BaseReading)),
            string.Concat(tokens.Select(token => token.Pronunciation)),
            first.PartsOfSpeech,
            first.ConjugationType,
            first.ConjugationForm,
            first.AType,
            first.StartOffset);
    }
}

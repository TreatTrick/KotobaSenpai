namespace KotobaSenpai.Core.Models;

/// <summary>
/// A phrase group part: an ordered, contiguous, non-empty sequence of token references. There must be no gaps
/// within a part; the surface and reading are derived locally from the referenced tokens, not trusted from model text.
/// </summary>
public sealed record PhraseGroupPart
{
    public PhraseGroupPart(IReadOnlyList<SentenceTokenReference> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
            throw new ArgumentException("A phrase part must reference at least one token.", nameof(tokens));

        Tokens = tokens.ToArray();
        Surface = string.Concat(Tokens.Select(reference => reference.Token.Surface));
        Reading = string.Concat(Tokens.Select(reference => reference.Token.Reading));
    }

    /// <summary>Token references in reading order, contiguous with no gaps.</summary>
    public IReadOnlyList<SentenceTokenReference> Tokens { get; }

    /// <summary>Display surface text concatenated locally from the referenced tokens.</summary>
    public string Surface { get; }

    /// <summary>Display reading concatenated locally from the referenced tokens.</summary>
    public string Reading { get; }
}
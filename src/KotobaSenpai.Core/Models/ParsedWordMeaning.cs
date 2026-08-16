namespace KotobaSenpai.Core.Models;

/// <summary>
/// A provider-returned, not-yet-validated per-word meaning (line-level model). The word references a local merged span
/// by its <see cref="Headword"/> (the surface the model copied from the request's local-chunk table); the orchestrator
/// matches it back to a local span by exact surface and builds the overlay view; unmatched words are dropped individually.
/// </summary>
public sealed record ParsedWordMeaning
{
    public const int MaxWordsPerSegment = 32;
    public const int MaxHeadwordLength = 64;
    public const int MaxPosLength = 32;
    public const int MaxMeaningLength = 256;
    public const int MaxGrammarLength = 512;

    public ParsedWordMeaning(
        string headword,
        string pos,
        string meaning,
        string grammar)
    {
        ArgumentNullException.ThrowIfNull(headword);
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(meaning);
        ArgumentNullException.ThrowIfNull(grammar);

        Headword = headword;
        Pos = pos;
        Meaning = meaning;
        Grammar = grammar;
    }

    /// <summary>The merged surface the model copied from the request's local-chunk table (the local span it annotates).</summary>
    public string Headword { get; }

    /// <summary>Contextual part of speech (e.g. 自動・カ変, 他動・五段, 名詞).</summary>
    public string Pos { get; }

    public string Meaning { get; }

    public string Grammar { get; }
}
namespace KotobaSenpai.Core.Models;

/// <summary>
/// A provider-returned, not-yet-validated group (line-level model). Parts store strongly typed token
/// reference ids rather than references; the orchestrator resolves them via the request-scoped id-to-reference
/// mapping and validates them into <see cref="PhraseGroup"/>; invalid groups are dropped individually.
/// </summary>
public sealed record ParsedPhraseGroup
{
    public ParsedPhraseGroup(
        string modelGroupId,
        string type,
        IReadOnlyList<IReadOnlyList<SentenceTokenId>> partTokenIds,
        string label,
        string meaning,
        string grammar)
    {
        ArgumentNullException.ThrowIfNull(modelGroupId);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(partTokenIds);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(meaning);
        ArgumentNullException.ThrowIfNull(grammar);

        ModelGroupId = modelGroupId;
        Type = type;
        PartTokenIds = partTokenIds.Select(ids => (IReadOnlyList<SentenceTokenId>)ids.ToArray()).ToArray();
        Label = label;
        Meaning = meaning;
        Grammar = grammar;
    }

    /// <summary>The group id returned by the model within a request.</summary>
    public string ModelGroupId { get; }

    public string Type { get; }

    /// <summary>Each part is a list of strongly typed token ids; parts may be separated from each other, but tokens within a part must be contiguous.</summary>
    public IReadOnlyList<IReadOnlyList<SentenceTokenId>> PartTokenIds { get; }

    public string Label { get; }

    public string Meaning { get; }

    public string Grammar { get; }
}
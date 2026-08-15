namespace KotobaSenpai.Core.Models;

/// <summary>
/// A validated phrase group. The model provides only the request-scoped <see cref="ModelGroupId"/>; the
/// application assigns <see cref="SessionGroupId"/> after validation and uses it as the shared identity of all
/// parts. The provider order is preserved via <see cref="ProviderOrder"/> and used to break ties on hover overlap.
/// </summary>
public sealed record PhraseGroup
{
    public const int MaxGroupsPerSegment = 8;
    public const int MaxLabelLength = 64;
    public const int MaxMeaningLength = 256;
    public const int MaxGrammarLength = 512;

    public PhraseGroup(
        string modelGroupId,
        string type,
        IReadOnlyList<PhraseGroupPart> parts,
        string label,
        string meaning,
        string grammar,
        Guid? sessionGroupId = null,
        int providerOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(modelGroupId))
            throw new ArgumentException("Model group id must not be empty.", nameof(modelGroupId));
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
            throw new ArgumentException("A phrase group must contain at least one part.", nameof(parts));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("A phrase group label must not be empty.", nameof(label));
        if (string.IsNullOrWhiteSpace(meaning))
            throw new ArgumentException("A phrase group meaning must not be empty.", nameof(meaning));
        if (string.IsNullOrWhiteSpace(grammar))
            throw new ArgumentException("A phrase group grammar explanation must not be empty.", nameof(grammar));
        if (providerOrder < 0)
            throw new ArgumentOutOfRangeException(nameof(providerOrder));

        ModelGroupId = modelGroupId;
        Type = type;
        Parts = parts.ToArray();
        Label = label;
        Meaning = meaning;
        Grammar = grammar;
        SessionGroupId = sessionGroupId ?? Guid.Empty;
        ProviderOrder = providerOrder;
    }

    /// <summary>The group id returned by the model within a request (unique only within the request, not across requests).</summary>
    public string ModelGroupId { get; }

    public string Type { get; }

    /// <summary>One or more contiguous parts; parts may be separated by other tokens.</summary>
    public IReadOnlyList<PhraseGroupPart> Parts { get; }

    public string Label { get; }

    public string Meaning { get; }

    public string Grammar { get; }

    /// <summary>Application-assigned session group id; non-empty after validation, serving as the shared identity of all parts/hovers/popups.</summary>
    public Guid SessionGroupId { get; }

    /// <summary>The order of appearance in the provider response (starting at 0).</summary>
    public int ProviderOrder { get; }

    /// <summary>The number of distinct tokens referenced by this group, used to break hover-overlap ties (fewer wins).</summary>
    public int DistinctTokenCount => Parts.SelectMany(part => part.Tokens).Select(token => token.Id).Distinct().Count();

    public PhraseGroup WithSessionId(Guid sessionGroupId) => new(
        ModelGroupId, Type, Parts, Label, Meaning, Grammar, sessionGroupId, ProviderOrder);

    public PhraseGroup WithProviderOrder(int providerOrder) => new(
        ModelGroupId, Type, Parts, Label, Meaning, Grammar, SessionGroupId, providerOrder);
}
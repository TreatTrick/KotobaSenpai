using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Validates the provider-returned line-level groups and converts them to <see cref="PhraseGroup"/>.
/// Invalid groups are dropped individually without affecting valid groups or local results; at most
/// <see cref="PhraseGroup.MaxGroupsPerSegment"/> are kept.
/// </summary>
public static class PhraseGroupValidator
{
    public sealed record ValidationResult(
        IReadOnlyList<PhraseGroup> ValidGroups,
        int DroppedCount,
        IReadOnlyList<string> Warnings);

    public static ValidationResult ValidateAndBuild(
        IReadOnlyList<ParsedPhraseGroup> parsedGroups,
        IReadOnlyDictionary<SentenceTokenId, SentenceTokenReference> tokenById)
    {
        ArgumentNullException.ThrowIfNull(parsedGroups);
        ArgumentNullException.ThrowIfNull(tokenById);

        var valid = new List<PhraseGroup>();
        var warnings = new List<string>();
        var dropped = 0;
        var seenModelIds = new HashSet<string>();

        foreach (var parsed in parsedGroups)
        {
            if (valid.Count >= PhraseGroup.MaxGroupsPerSegment)
            {
                warnings.Add($"Dropping groups beyond the {PhraseGroup.MaxGroupsPerSegment}-group limit.");
                dropped++;
                continue;
            }

            var reason = FirstInvalidReason(parsed, tokenById, seenModelIds);
            if (reason is not null)
            {
                warnings.Add($"Dropping group '{parsed.ModelGroupId}': {reason}");
                dropped++;
                continue;
            }

            seenModelIds.Add(parsed.ModelGroupId);
            var parts = parsed.PartTokenIds
                .Select(ids => new PhraseGroupPart(ids.Select(id => tokenById[id]).ToArray()))
                .ToArray();
            valid.Add(new PhraseGroup(
                parsed.ModelGroupId,
                parsed.Type,
                parts,
                parsed.Label,
                parsed.Meaning,
                parsed.Grammar,
                providerOrder: valid.Count));
        }

        return new ValidationResult(valid, dropped, warnings);
    }

    private static string? FirstInvalidReason(
        ParsedPhraseGroup group,
        IReadOnlyDictionary<SentenceTokenId, SentenceTokenReference> tokenById,
        HashSet<string> seenModelIds)
    {
        if (string.IsNullOrWhiteSpace(group.ModelGroupId))
            return "missing model group id";
        if (!seenModelIds.Add(group.ModelGroupId))
            return "duplicate model group id";
        if (string.IsNullOrWhiteSpace(group.Label))
            return "missing label";
        if (string.IsNullOrWhiteSpace(group.Meaning))
            return "missing meaning";
        if (string.IsNullOrWhiteSpace(group.Grammar))
            return "missing grammar explanation";
        if (group.Label.Length > PhraseGroup.MaxLabelLength)
            return "label too long";
        if (group.Meaning.Length > PhraseGroup.MaxMeaningLength)
            return "meaning too long";
        if (group.Grammar.Length > PhraseGroup.MaxGrammarLength)
            return "grammar explanation too long";
        if (group.PartTokenIds.Count == 0)
            return "no parts";

        var usedTokenIds = new HashSet<SentenceTokenId>();
        foreach (var partIds in group.PartTokenIds)
        {
            if (partIds.Count == 0)
                return "empty part";
            SentenceTokenReference? previous = null;
            foreach (var id in partIds)
            {
                if (!tokenById.TryGetValue(id, out var reference))
                    return $"unknown token id '{id}'";
                if (!usedTokenIds.Add(id))
                    return $"token '{id}' referenced more than once";
                if (previous is not null && reference.SentenceIndex != previous.SentenceIndex + 1)
                    return $"non-contiguous part token(s) '{previous.Id}','{id}'";
                previous = reference;
            }
        }
        return null;
    }
}
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Validates the provider-returned per-word meanings against the request's local merged spans and converts them to
/// <see cref="WordMeaningView"/>. A word is matched back to a local span by exact surface (the headword the model copied
/// from the request's local-chunk table), so a mis-numbered or extra word only drops itself rather than cascading.
/// Invalid/unmatched words are dropped individually; at most <see cref="ParsedWordMeaning.MaxWordsPerSegment"/> are kept.
/// </summary>
public static class WordMeaningValidator
{
    public sealed record ValidationResult(
        IReadOnlyList<WordMeaningView> ValidWords,
        int DroppedCount,
        IReadOnlyList<string> Warnings);

    public static ValidationResult ValidateAndBuild(
        IReadOnlyList<ParsedWordMeaning> parsedWords,
        IReadOnlyList<LocalSpanSummary> localSpans)
    {
        ArgumentNullException.ThrowIfNull(parsedWords);
        ArgumentNullException.ThrowIfNull(localSpans);

        // A word can appear multiple times in a sentence (same surface across separate spans), so key by surface but keep the first occurrence rather than throwing on a duplicate.
        var bySurface = new Dictionary<string, LocalSpanSummary>(StringComparer.Ordinal);
        foreach (var span in localSpans)
            bySurface.TryAdd(span.Surface, span);
        var valid = new List<WordMeaningView>();
        var warnings = new List<string>();
        var dropped = 0;
        var matchedSurfaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parsed in parsedWords)
        {
            if (valid.Count >= ParsedWordMeaning.MaxWordsPerSegment)
            {
                warnings.Add($"Dropping words beyond the {ParsedWordMeaning.MaxWordsPerSegment}-word limit.");
                dropped++;
                continue;
            }
            var reason = FirstInvalidReason(parsed, bySurface, matchedSurfaces);
            if (reason is not null)
            {
                warnings.Add($"Dropping word '{parsed.Headword}': {reason}");
                dropped++;
                continue;
            }

            matchedSurfaces.Add(parsed.Headword);
            var span = bySurface[parsed.Headword];
            valid.Add(new WordMeaningView(
                span.Surface, span.Reading, parsed.Pos, parsed.Meaning, parsed.Grammar));
        }

        return new ValidationResult(valid, dropped, warnings);
    }

    private static string? FirstInvalidReason(
        ParsedWordMeaning word,
        IReadOnlyDictionary<string, LocalSpanSummary> bySurface,
        HashSet<string> matchedSurfaces)
    {
        if (string.IsNullOrWhiteSpace(word.Headword))
            return "missing headword";
        if (word.Headword.Length > ParsedWordMeaning.MaxHeadwordLength)
            return "headword too long";
        if (!bySurface.TryGetValue(word.Headword, out _))
            return $"no local span with surface '{word.Headword}'";
        if (!matchedSurfaces.Add(word.Headword))
            return $"headword '{word.Headword}' annotated more than once";
        if (string.IsNullOrWhiteSpace(word.Pos))
            return "missing pos";
        if (string.IsNullOrWhiteSpace(word.Meaning))
            return "missing meaning";
        if (string.IsNullOrWhiteSpace(word.Grammar))
            return "missing grammar explanation";
        if (word.Pos.Length > ParsedWordMeaning.MaxPosLength)
            return "pos too long";
        if (word.Meaning.Length > ParsedWordMeaning.MaxMeaningLength)
            return "meaning too long";
        if (word.Grammar.Length > ParsedWordMeaning.MaxGrammarLength)
            return "grammar explanation too long";
        return null;
    }
}
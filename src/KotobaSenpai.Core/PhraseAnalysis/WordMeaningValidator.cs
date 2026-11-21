using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.PhraseAnalysis;

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

        var bySurface = localSpans.GroupBy(span => span.Surface, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<LocalSpanSummary>(group), StringComparer.Ordinal);
        var valid = new List<WordMeaningView>();
        var warnings = new List<string>();
        var dropped = 0;

        foreach (var parsed in parsedWords)
        {
            if (valid.Count >= ParsedWordMeaning.MaxWordsPerSegment)
            {
                warnings.Add($"Dropping words beyond the {ParsedWordMeaning.MaxWordsPerSegment}-word limit.");
                dropped++;
                continue;
            }
            // Consume a matching occurrence even if its annotation is invalid, so later annotations stay aligned.
            LocalSpanSummary? span = null;
            if (bySurface.TryGetValue(parsed.Headword, out var occurrences))
                occurrences.TryDequeue(out span);
            var reason = FirstInvalidReason(parsed, span);
            if (reason is not null)
            {
                warnings.Add($"Dropping word '{parsed.Headword}': {reason}");
                dropped++;
                continue;
            }

            var local = span!;
            var overrideReading = GetReadingOverride(parsed, local, warnings);
            var pitchUnknown = local.HasOverriddenReading || overrideReading is not null;
            valid.Add(new WordMeaningView(local.Surface, overrideReading ?? local.Reading,
                parsed.Pos, parsed.Meaning, parsed.Grammar, pitchUnknown ? [] : local.PitchAccents)
            {
                SpanIndex = local.SpanIndex,
                HasOverriddenReading = pitchUnknown,
            });
        }

        return new ValidationResult(valid, dropped, warnings);
    }

    private static string? GetReadingOverride(ParsedWordMeaning parsed, LocalSpanSummary span, List<string> warnings)
    {
        if (string.IsNullOrEmpty(parsed.Reading))
            return null;
        var reading = Kana.ToHiragana(parsed.Reading);
        if (reading.Length > ParsedWordMeaning.MaxReadingLength || !JapaneseText.IsHiraganaReading(reading))
        {
            warnings.Add($"Ignoring invalid reading for word '{span.Surface}': expected 1–64 kana characters.");
            return null;
        }
        if (!JapaneseText.ContainsKanji(span.Surface) || reading == Kana.ToHiragana(span.Reading))
            return null;
        return reading;
    }

    private static string? FirstInvalidReason(ParsedWordMeaning word, LocalSpanSummary? span)
    {
        if (string.IsNullOrWhiteSpace(word.Headword))
            return "missing headword";
        if (word.Headword.Length > ParsedWordMeaning.MaxHeadwordLength)
            return "headword too long";
        if (span is null)
            return "no remaining local span for this headword";
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

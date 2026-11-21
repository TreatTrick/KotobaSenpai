using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.WordGrouping;

internal sealed record CorrectedWordPitchResolution(
    WordPitchReference WordPitch,
    IReadOnlyList<PitchAccentSummary> Segments);

internal static class CorrectedWordPitch
{
    internal static WordPitchReference? Find(GroupedWord word, IWordPitchLookup lookup)
        => Resolve(word, lookup)?.WordPitch;

    internal static CorrectedWordPitchResolution? Resolve(GroupedWord word, IWordPitchLookup lookup)
    {
        if (FindChangedToken(word) is not { } correction)
            return null;

        var token = word.SourceTokens[correction.TokenIndex];
        if (!JapaneseText.ContainsKanji(token.Surface))
            return null;

        var positions = lookup.FindWordPitch(token, correction.Reading);
        if (positions.Count == 0)
            return null;

        var wordPitch = new WordPitchReference(token.Surface, correction.Reading, positions);
        var segments = BuildSegments(word.SourceTokens, correction.TokenIndex, correction.Reading, positions[0]);
        return new(wordPitch, segments);
    }

    private static IReadOnlyList<PitchAccentSummary> BuildSegments(
        IReadOnlyList<Token> tokens,
        int changedTokenIndex,
        string correctedReading,
        int correctedPosition)
    {
        var result = new List<PitchAccentSummary>(tokens.Count);
        var offset = 0;
        var index = 0;
        while (index < tokens.Count)
        {
            if (index == changedTokenIndex)
            {
                var changed = tokens[index];
                var pattern = PitchAccent.CreatePattern(correctedReading, correctedPosition);
                result.Add(new PitchAccentSummary(
                    changed.Surface,
                    correctedReading,
                    offset,
                    pattern?.AccentPosition,
                    pattern?.Notation));
                offset += changed.Surface.Length;
                index++;
                continue;
            }

            var runEnd = FindUnchangedInflectionEnd(tokens, index, changedTokenIndex);
            if (runEnd > index + 1)
            {
                var run = tokens.Skip(index).Take(runEnd - index).ToArray();
                var surface = string.Concat(run.Select(t => t.Surface));
                var reading = string.Concat(run.Select(t => Kana.ToHiragana(t.Reading)));
                var position = MorphologicalPitchRules.ResolvePosition(run);
                var pattern = PitchAccent.CreatePattern(reading, position);
                result.Add(new PitchAccentSummary(
                    surface,
                    reading,
                    offset,
                    pattern?.AccentPosition,
                    pattern?.Notation));
                offset += surface.Length;
                index = runEnd;
                continue;
            }

            var token = tokens[index];
            var singlePosition = MorphologicalPitchRules.ResolvePosition([token]) ?? token.PitchAccentPosition;
            var singlePattern = PitchAccent.CreatePattern(token.Reading, singlePosition);
            result.Add(new PitchAccentSummary(
                token.Surface,
                token.Reading,
                offset,
                singlePattern?.AccentPosition,
                singlePattern?.Notation));
            offset += token.Surface.Length;
            index++;
        }

        return result;
    }

    private static int FindUnchangedInflectionEnd(IReadOnlyList<Token> tokens, int start, int changedTokenIndex)
    {
        var first = tokens[start];
        if (!IsInflectingBase(first))
            return start + 1;

        var end = start + 1;
        while (end < tokens.Count
            && end != changedTokenIndex
            && IsInflectionContinuation(tokens[end]))
        {
            end++;
        }
        return end;
    }

    private static bool IsInflectingBase(Token token)
        => token.PartsOfSpeech.Pos1 is "動詞" or "形容詞" or "助動詞"
            || (token.PartsOfSpeech.Pos1 == "接尾辞" && token.PartsOfSpeech.Pos2 == "形容詞的");

    private static bool IsInflectionContinuation(Token token)
        => token.PartsOfSpeech.Pos1 == "助動詞"
            || (token.PartsOfSpeech.Pos1 == "助詞"
                && token.PartsOfSpeech.Pos2 == "接続助詞"
                && token.Surface is "て" or "で");

    private static (int TokenIndex, string Reading)? FindChangedToken(GroupedWord word)
    {
        var readings = word.SourceTokens.Select(token => Kana.ToHiragana(token.Reading)).ToArray();
        var effectiveReading = Kana.ToHiragana(word.Reading);
        if (readings.Any(string.IsNullOrEmpty) || !JapaneseText.IsHiraganaReading(effectiveReading))
            return null;

        // Only assign a new reading when all the other token readings anchor one changed token.
        int? matchedTokenIndex = null;
        string matchedReading = string.Empty;
        for (var index = 0; index < readings.Length; index++)
        {
            var prefix = string.Concat(readings.Take(index));
            var suffix = string.Concat(readings.Skip(index + 1));
            if (prefix.Length + suffix.Length >= effectiveReading.Length
                || !effectiveReading.StartsWith(prefix, StringComparison.Ordinal)
                || !effectiveReading.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            var reading = effectiveReading[prefix.Length..(effectiveReading.Length - suffix.Length)];
            if (reading == readings[index])
                continue;
            if (matchedTokenIndex is not null)
                return null;

            matchedTokenIndex = index;
            matchedReading = reading;
        }

        return matchedTokenIndex is int tokenIndex ? (tokenIndex, matchedReading) : null;
    }
}

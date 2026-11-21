using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.WordGrouping;

internal static class CorrectedWordPitch
{
    internal static WordPitchReference? Find(GroupedWord word, IWordPitchLookup lookup)
    {
        if (FindChangedToken(word) is not { } correction)
            return null;

        var token = word.SourceTokens[correction.TokenIndex];
        if (!JapaneseText.ContainsKanji(token.Surface))
            return null;

        var positions = lookup.FindWordPitch(token, correction.Reading);
        return positions.Count == 0
            ? null
            : new(token.Surface, correction.Reading, positions);
    }

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

using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.WordGrouping;

internal static class CorrectedWordPitch
{
    internal static WordPitchReference? Find(GroupedWord word, IWordPitchLookup lookup)
    {
        var tokens = word.SourceTokens;
        var readings = tokens.Select(token => Kana.ToHiragana(token.Reading)).ToArray();
        var effectiveReading = Kana.ToHiragana(word.Reading);
        if (readings.Any(string.IsNullOrEmpty) || !JapaneseText.IsHiraganaReading(effectiveReading))
            return null;

        // Only assign a new reading when all the other token readings anchor one changed token.
        Token? matchedToken = null;
        string matchedReading = string.Empty;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (!JapaneseText.ContainsKanji(token.Surface))
                continue;

            var prefix = string.Concat(readings.Take(index));
            var suffix = string.Concat(readings.Skip(index + 1));
            if (prefix.Length + suffix.Length >= effectiveReading.Length
                || !effectiveReading.StartsWith(prefix, StringComparison.Ordinal)
                || !effectiveReading.EndsWith(suffix, StringComparison.Ordinal))
                continue;

            var reading = effectiveReading[prefix.Length..(effectiveReading.Length - suffix.Length)];
            if (reading == readings[index])
                continue;
            if (matchedToken is not null)
                return null;

            matchedToken = token;
            matchedReading = reading;
        }

        if (matchedToken is null)
            return null;

        var positions = lookup.FindWordPitch(matchedToken, matchedReading);
        return positions.Count == 0 ? null : new(matchedToken.Surface, matchedReading, positions);
    }
}

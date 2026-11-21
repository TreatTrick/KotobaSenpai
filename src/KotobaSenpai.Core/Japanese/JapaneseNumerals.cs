using System.Text.RegularExpressions;

namespace KotobaSenpai.Core.Japanese;

internal static partial class JapaneseNumerals
{
    private const string Digits = "〇一二三四五六七八九";

    [GeneratedRegex("[0-9０-９]+")]
    private static partial Regex ArabicRuns();

    internal static string Normalize(string surface)
        => ArabicRuns().Replace(surface, match => TryParseArabic(match.ValueSpan, out var number)
            ? Format(number)
            : match.Value);

    internal static bool IsArabicDigit(char character) => character is >= '0' and <= '9' or >= '０' and <= '９';

    internal static bool IsNumericCharacter(char character)
        => IsArabicDigit(character) || Digits.Contains(character) || "零十百千万億兆".Contains(character);

    internal static bool TryParse(string text, out int number)
    {
        if (TryParseArabic(text, out number))
            return true;

        // Match canonical Japanese numerals to avoid guessing nonstandard readings.
        var tenIndex = text.IndexOf('十');
        if (tenIndex < 0)
        {
            number = text.Length == 1 ? Digits.IndexOf(text[0]) : 0;
            return number > 0;
        }
        var tens = tenIndex switch { 0 => 1, 1 => Digits.IndexOf(text[0]), _ => 0 };
        var units = tenIndex == text.Length - 1 ? 0 : Digits.IndexOf(text[^1]);
        number = tens * 10 + units;
        return tens > 0 && units >= 0 && Format(number) == text;
    }

    private static bool TryParseArabic(ReadOnlySpan<char> text, out int number)
    {
        number = 0;
        if (text.Length is < 1 or > 2 || text[0] is '0' or '０')
            return false;
        foreach (var character in text)
        {
            if (!IsArabicDigit(character))
                return false;
            number = number * 10 + (character <= '9' ? character - '0' : character - '０');
        }
        return number > 0;
    }

    private static string Format(int number) => number switch
    {
        < 10 => Digits[number].ToString(),
        < 20 => "十" + (number % 10 == 0 ? "" : Digits[number % 10].ToString()),
        _ => Digits[number / 10] + "十" + (number % 10 == 0 ? "" : Digits[number % 10].ToString()),
    };
}

namespace KotobaSenpai.Core.Japanese;

internal static class JapaneseText
{
    internal static bool ContainsKanji(string text)
        => text.EnumerateRunes().Any(rune => rune.Value is >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
            or >= 0x20000 and <= 0x2FA1F or >= 0x30000 and <= 0x323AF);

    internal static bool IsHiraganaReading(string reading)
        => reading.Length > 0 && reading.All(character => character is >= 'ぁ' and <= 'ゖ' or 'ゝ' or 'ゞ' or 'ー');
}

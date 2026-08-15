namespace KotobaSenpai.Core.Japanese;

/// <summary>Hiragana/katakana conversion utilities.</summary>
public static class Kana
{
    /// <summary>Converts katakana to hiragana; non-katakana characters are kept as-is. Reading tables uniformly store hiragana, normalized during lookup.</summary>
    public static string ToHiragana(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c >= 'ァ' && c <= 'ヶ')
                chars[i] = (char)(c - 0x60);
            else if (c == 'ヽ') // ヽ → ゝ
                chars[i] = 'ゝ';
            else if (c == 'ヾ') // ヾ → ゞ
                chars[i] = 'ゞ';
        }
        return new string(chars);
    }
}
namespace KotobaSenpai.Core.Japanese;

/// <summary>平假/片假名转换工具。</summary>
public static class Kana
{
    /// <summary>把片假名转为平假名；非片假名字符原样保留。读音表统一存平假名，查词时归一化。</summary>
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
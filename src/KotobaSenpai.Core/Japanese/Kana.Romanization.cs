using System.Text;

namespace KotobaSenpai.Core.Japanese;

public static partial class Kana
{
    private static readonly IReadOnlyDictionary<string, string> Romaji = BuildRomanizationTable();

    /// <summary>Romanizes kana readings with Hepburn consonants and explicit, ASCII long vowels.</summary>
    public static string ToRomaji(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var reading = ToHiragana(text.Normalize(NormalizationForm.FormKC));
        var result = new StringBuilder(reading.Length * 2);
        for (var index = 0; index < reading.Length; index++)
        {
            if (reading[index] == 'っ')
            {
                var next = RomanizedSyllable(reading, index + 1, out _);
                result.Append(next.Length > 0 && char.IsAsciiLetterLower(next[0]) && !"aeiouny".Contains(next[0])
                    ? next.StartsWith("ch", StringComparison.Ordinal) ? "t" : next[..1]
                    : "tsu");
                continue;
            }
            if (reading[index] == 'ん')
            {
                var next = RomanizedSyllable(reading, index + 1, out _);
                result.Append(next.Length > 0 && "aeiouy".Contains(next[0]) ? "n'" : "n");
                continue;
            }
            if (reading[index] == 'ー' && result.Length > 0 && "aeiou".Contains(result[^1]))
            {
                result.Append(result[^1]);
                continue;
            }
            result.Append(RomanizedSyllable(reading, index, out var consumed));
            index += consumed - 1;
        }
        return result.ToString();
    }

    private static string RomanizedSyllable(string reading, int index, out int consumed)
    {
        consumed = 1;
        if (index >= reading.Length)
            return string.Empty;
        if (index + 1 < reading.Length && Romaji.TryGetValue(reading.Substring(index, 2), out var compound))
        {
            consumed = 2;
            return compound;
        }
        var kana = reading[index].ToString();
        return Romaji.TryGetValue(kana, out var syllable) ? syllable : kana;
    }

    private static Dictionary<string, string> BuildRomanizationTable()
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        void AddRow(string kana, string syllables)
        {
            var values = syllables.Split(' ');
            for (var index = 0; index < kana.Length; index++)
                table.Add(kana[index].ToString(), values[index]);
        }

        AddRow("あいうえお", "a i u e o");
        AddRow("かきくけこ", "ka ki ku ke ko");
        AddRow("さしすせそ", "sa shi su se so");
        AddRow("たちつてと", "ta chi tsu te to");
        AddRow("なにぬねの", "na ni nu ne no");
        AddRow("はひふへほ", "ha hi fu he ho");
        AddRow("まみむめも", "ma mi mu me mo");
        AddRow("やゆよ", "ya yu yo");
        AddRow("らりるれろ", "ra ri ru re ro");
        AddRow("わゐゑをん", "wa wi we wo n");
        AddRow("がぎぐげご", "ga gi gu ge go");
        AddRow("ざじずぜぞ", "za ji zu ze zo");
        AddRow("だぢづでど", "da ji zu de do");
        AddRow("ばびぶべぼ", "ba bi bu be bo");
        AddRow("ぱぴぷぺぽ", "pa pi pu pe po");
        AddRow("ぁぃぅぇぉゃゅょゎゔゕゖ", "a i u e o ya yu yo wa vu ka ke");
        foreach (var (kana, consonant) in new[]
        {
            ("き", "ky"), ("ぎ", "gy"), ("し", "sh"), ("じ", "j"), ("ち", "ch"), ("ぢ", "j"),
            ("に", "ny"), ("ひ", "hy"), ("び", "by"), ("ぴ", "py"), ("み", "my"), ("り", "ry"),
        })
        {
            table.Add(kana + "ゃ", consonant + "a");
            table.Add(kana + "ゅ", consonant + "u");
            table.Add(kana + "ょ", consonant + "o");
        }
        foreach (var (kana, syllable) in new[]
        {
            ("いぇ", "ye"), ("うぃ", "wi"), ("うぇ", "we"), ("うぉ", "wo"),
            ("ゔぁ", "va"), ("ゔぃ", "vi"), ("ゔぇ", "ve"), ("ゔぉ", "vo"), ("ゔゅ", "vyu"),
            ("ふぁ", "fa"), ("ふぃ", "fi"), ("ふぇ", "fe"), ("ふぉ", "fo"), ("ふゅ", "fyu"),
            ("しぇ", "she"), ("じぇ", "je"), ("ちぇ", "che"),
            ("てぃ", "ti"), ("てゅ", "tyu"), ("でぃ", "di"), ("でゅ", "dyu"), ("とぅ", "tu"), ("どぅ", "du"),
            ("つぁ", "tsa"), ("つぃ", "tsi"), ("つぇ", "tse"), ("つぉ", "tso"),
            ("くぁ", "kwa"), ("くぃ", "kwi"), ("くぇ", "kwe"), ("くぉ", "kwo"),
            ("ぐぁ", "gwa"), ("ぐぃ", "gwi"), ("ぐぇ", "gwe"), ("ぐぉ", "gwo"),
        })
            table.Add(kana, syllable);
        return table;
    }
}

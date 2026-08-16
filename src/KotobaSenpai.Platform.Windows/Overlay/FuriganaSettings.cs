using System.Globalization;
using System.Text.RegularExpressions;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>settings.json 键、默认值与纯逻辑：振假名字号占 OCR 文字高度（词外接框高度）的比例。</summary>
public static class FuriganaSettings
{
    public const string FontScaleKey = "FuriganaFontScale";
    public const double DefaultFontScale = 1.0 / 2.0;

    private static readonly Regex CJKIdeograph = new(@"\p{IsCJKUnifiedIdeographs}", RegexOptions.Compiled);

    /// <summary>True when the surface text contains at least one CJK ideograph (kanji); pure kana/katakana/other scripts are skipped.</summary>
    public static bool ContainsKanji(string text) => CJKIdeograph.IsMatch(text);

    /// <summary>
    /// Trims the trailing okurigana from a word's reading so only the kanji portion is annotated. Mirrors DokiDokiDict's
    /// algorithm: take the leading kanji run and drop as many trailing reading graphemes as there are trailing non-kanji
    /// surface glyphs. E.g. surface "同じ" reading "おなじ" → "おな"; "食べる"/"たべる" → "た".
    /// </summary>
    /// <param name="surface">The word's surface form.</param>
    /// <param name="reading">The word's full reading (kana).</param>
    /// <returns>The count of leading kanji glyphs and the reading restricted to the kanji portion.</returns>
    public static (int KanjiCharCount, string KanjiReading) OkuriganaTrim(string surface, string reading)
    {
        int kanjiChars = 0;
        while (kanjiChars < surface.Length && ContainsKanji(surface[kanjiChars].ToString()))
            kanjiChars++;

        int trailingKana = 0;
        for (int i = surface.Length - 1; i >= 0 && !ContainsKanji(surface[i].ToString()); i--)
            trailingKana++;

        var kanjiReading = kanjiChars > 0 && trailingKana > 0 && reading.Length > trailingKana
            ? reading[..^trailingKana]
            : reading;

        return (kanjiChars, kanjiReading);
    }

    /// <summary>Parses a stored font scale; missing/invalid/zero falls back to the default 1/3.</summary>
    public static double ResolveFontScale(string? raw)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0
            ? scale
            : DefaultFontScale;
}
using System.Globalization;
using System.Text.RegularExpressions;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Settings key, default value, and pure logic for the furigana size ratio.</summary>
public static class FuriganaSettings
{
    public const string FontScaleKey = "FuriganaFontScale";
    public const double DefaultFontScale = 1.0 / 3.0;
    public const string PitchHighColor = "#FF4444";
    public const string PitchLowColor = "#4488FF";
    public const string PitchHeibanColor = "#88FF88";

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

    /// <summary>Maps contiguous kana runs in a surface to the mora range used by a full pitch pattern.</summary>
    public static IReadOnlyList<(int SurfaceStart, int SurfaceLength, int MoraStart, int MoraCount)> GetPitchMoraRanges(
        string surface,
        int moraCount)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.Length == 0 || moraCount <= 0)
            return Array.Empty<(int, int, int, int)>();

        var ranges = new List<(int, int, int, int)>();
        for (var start = 0; start < surface.Length;)
        {
            if (!IsKana(surface[start]))
            {
                start++;
                continue;
            }

            var end = start + 1;
            while (end < surface.Length && IsKana(surface[end]))
                end++;

            var moraStart = Math.Clamp((int)Math.Round(start * moraCount / (double)surface.Length), 0, moraCount);
            if (moraStart < moraCount)
            {
                var moraEnd = Math.Clamp(
                    (int)Math.Round(end * moraCount / (double)surface.Length),
                    moraStart + 1,
                    moraCount);
                ranges.Add((start, end - start, moraStart, moraEnd - moraStart));
            }
            start = end;
        }
        return ranges;
    }

    /// <summary>Parses a stored font scale; missing, invalid, or zero values fall back to the default 1/3.</summary>
    public static double ResolveFontScale(string? raw)
        => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0
            ? scale
            : DefaultFontScale;

    private static bool IsKana(char character)
        => (character >= '\u3040' && character <= '\u30ff')
            || (character >= '\u31f0' && character <= '\u31ff');
}

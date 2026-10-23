using KotobaSenpai.Core.Japanese;
using System.Text.RegularExpressions;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>A surface range and the reading assigned to it.</summary>
public sealed record FuriganaSegment(int SurfaceStart, int SurfaceLength, string Reading, bool IsKana);

/// <summary>Pure furigana segmentation and pitch presentation rules.</summary>
public static class FuriganaSettings
{
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

    /// <summary>
    /// Distributes a reading over alternating kana and non-kana surface runs. Kana runs that match the reading are
    /// returned without a reading so callers can render pitch markers instead of duplicate furigana text.
    /// </summary>
    public static IReadOnlyList<FuriganaSegment> GetFuriganaSegments(string surface, string reading)
    {
        if (surface == "受け入れ")
        {
            Console.WriteLine($"Surface: {surface}, Reading: {reading}");
        }
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(reading);
        if (surface.Length == 0)
            return Array.Empty<FuriganaSegment>();
        if (surface == reading)
            return [new FuriganaSegment(0, surface.Length, string.Empty, surface.All(IsKana))];

        var groups = new List<(string Text, bool IsKana)>();
        for (var i = 0; i < surface.Length;)
        {
            var isKana = IsKana(surface[i]);
            var start = i++;
            while (i < surface.Length && IsKana(surface[i]) == isKana)
                i++;
            groups.Add((surface[start..i], isKana));
        }

        var normalizedReading = Kana.ToHiragana(reading);
        var normalizedGroups = groups
            .Select(group => (group.Text, group.IsKana, NormalizedText: group.IsKana ? Kana.ToHiragana(group.Text) : null))
            .ToArray();
        var distributed = Distribute(normalizedReading, reading, normalizedGroups, 0);
        if (distributed is null)
            return [new FuriganaSegment(0, surface.Length, reading, false)];

        var result = new List<FuriganaSegment>(distributed.Count);
        var surfaceOffset = 0;
        foreach (var segment in distributed)
        {
            result.Add(new FuriganaSegment(
                surfaceOffset,
                segment.Text.Length,
                segment.Reading,
                segment.IsKana));
            surfaceOffset += segment.Text.Length;
        }
        return result;
    }

    /// <summary>
    /// Maps surface kana runs to the corresponding mora ranges in a complete reading. Only runs that are not
    /// annotated with furigana are returned, keeping pitch dots mutually exclusive with furigana text.
    /// </summary>
    public static IReadOnlyList<(int SurfaceStart, int SurfaceLength, int MoraStart, int MoraCount)> GetPitchMoraRanges(
        string surface,
        string reading,
        int moraCount)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(reading);
        if (surface.Length == 0 || moraCount <= 0)
            return Array.Empty<(int, int, int, int)>();

        var segments = GetFuriganaSegments(surface, reading);
        var normalizedReading = Kana.ToHiragana(reading);
        var readingOffset = 0;
        var moraOffset = 0;
        var ranges = new List<(int SurfaceStart, int SurfaceLength, int MoraStart, int MoraCount)>();
        foreach (var segment in segments)
        {
            var text = surface.Substring(segment.SurfaceStart, segment.SurfaceLength);
            var normalizedText = Kana.ToHiragana(segment.IsKana ? text : segment.Reading);
            var segmentMoraCount = PitchAccent.SplitMorae(normalizedText).Count;
            if (segmentMoraCount == 0)
                continue;

            if (segment.IsKana
                && readingOffset + normalizedText.Length <= normalizedReading.Length
                && normalizedReading.AsSpan(readingOffset, normalizedText.Length).SequenceEqual(normalizedText.AsSpan()))
            {
                ranges.Add((segment.SurfaceStart, segment.SurfaceLength, moraOffset, segmentMoraCount));
            }

            readingOffset += normalizedText.Length;
            moraOffset += segmentMoraCount;
        }

        return ranges
            .Where(range => range.MoraStart < moraCount)
            .Select(range =>
            {
                var count = Math.Min(range.MoraCount, moraCount - range.MoraStart);
                return (
                    SurfaceStart: range.SurfaceStart,
                    SurfaceLength: range.SurfaceLength,
                    MoraStart: range.MoraStart,
                    MoraCount: count);
            })
            .Where(range => range.MoraCount > 0)
            .ToArray();
    }

    /// <summary>Maps contiguous kana runs in a surface to the mora range used by a full pitch pattern.</summary>
    public static IReadOnlyList<(int SurfaceStart, int SurfaceLength, int MoraStart, int MoraCount)> GetPitchMoraRanges(
        string surface,
        int moraCount)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.Length == 0 || moraCount <= 0)
            return Array.Empty<(int, int, int, int)>();

        var ranges = new List<(int SurfaceStart, int SurfaceLength, int MoraStart, int MoraCount)>();
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

    private static IReadOnlyList<(string Text, string Reading, bool IsKana)>? Distribute(
        string normalizedReading,
        string originalReading,
        IReadOnlyList<(string Text, bool IsKana, string? NormalizedText)> groups,
        int groupStart)
    {
        if (groupStart >= groups.Count)
            return normalizedReading.Length == 0 ? Array.Empty<(string, string, bool)>() : null;

        var group = groups[groupStart];
        if (group.IsKana)
        {
            if (group.NormalizedText is null || !normalizedReading.StartsWith(group.NormalizedText, StringComparison.Ordinal))
                return null;
            var next = Distribute(
                normalizedReading[group.NormalizedText.Length..],
                originalReading[group.NormalizedText.Length..],
                groups,
                groupStart + 1);
            if (next is null)
                return null;
            return [(group.Text, string.Empty, true), .. next];
        }

        IReadOnlyList<(string Text, string Reading, bool IsKana)>? result = null;
        for (var i = normalizedReading.Length; i >= group.Text.Length; i--)
        {
            var next = Distribute(
                normalizedReading[i..],
                originalReading[i..],
                groups,
                groupStart + 1);
            if (next is null)
                continue;
            if (result is not null)
                return null;
            result = [(group.Text, originalReading[..i], false), .. next];
            if (groups.Count - groupStart == 1)
                break;
        }
        return result;
    }

    private static bool IsKana(char character)
        => (character >= '\u3040' && character <= '\u30ff')
            || (character >= '\u31f0' && character <= '\u31ff');
}

namespace KotobaSenpai.Core.Japanese;

/// <summary>Pure UniDic pitch-accent parsing and Tokyo-style mora pattern helpers.</summary>
public static class PitchAccent
{
    private static readonly HashSet<char> SmallKana =
        "ゃゅょぁぃぅぇぉゎャュョァィゥェォヮ".ToHashSet();

    public static int? ParsePosition(string? raw, string? reading = null)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "*")
            return null;

        foreach (var candidate in raw.Split(','))
        {
            if (!int.TryParse(candidate.Trim(), out var position) || position < 0)
                continue;
            if (reading is not null && position > SplitMorae(reading).Count)
                continue;
            return position;
        }

        return null;
    }

    public static IReadOnlyList<string> SplitMorae(string? reading)
    {
        if (string.IsNullOrEmpty(reading))
            return Array.Empty<string>();

        var morae = new List<string>();
        foreach (var character in reading)
        {
            if (SmallKana.Contains(character) && morae.Count > 0)
                morae[^1] += character;
            else
                morae.Add(character.ToString());
        }
        return morae;
    }

    public static IReadOnlyList<bool> BuildPattern(int accentPosition, int moraCount)
    {
        if (moraCount <= 0)
            return Array.Empty<bool>();
        if (accentPosition < 0 || accentPosition > moraCount)
            throw new ArgumentOutOfRangeException(nameof(accentPosition));
        if (moraCount == 1)
            return [true];
        if (accentPosition == 0)
            return [false, .. Enumerable.Repeat(true, moraCount - 1)];
        if (accentPosition == 1)
            return [true, .. Enumerable.Repeat(false, moraCount - 1)];

        var pattern = new bool[moraCount];
        for (var i = 1; i < moraCount; i++)
            pattern[i] = i < accentPosition;
        return pattern;
    }

    public static string Format(int accentPosition, IReadOnlyList<bool> highMorae)
    {
        ArgumentNullException.ThrowIfNull(highMorae);
        var text = new System.Text.StringBuilder($"[{accentPosition}] ");
        for (var i = 0; i < highMorae.Count; i++)
        {
            var high = highMorae[i];
            text.Append(high ? 'H' : 'L');
            if (high && i + 1 < highMorae.Count && !highMorae[i + 1])
                text.Append('↓');
        }
        if (accentPosition == highMorae.Count && accentPosition > 0)
            text.Append('↓');
        return text.ToString();
    }

    public static PitchAccentPattern? CreatePattern(string? reading, int? accentPosition)
    {
        var morae = SplitMorae(reading);
        if (accentPosition is null || morae.Count == 0 || accentPosition > morae.Count)
            return null;

        var pattern = BuildPattern(accentPosition.Value, morae.Count);
        return new PitchAccentPattern(
            accentPosition.Value,
            morae,
            pattern,
            Format(accentPosition.Value, pattern));
    }
}

/// <summary>One immutable pitch pattern aligned with a token's reading morae.</summary>
public sealed record PitchAccentPattern(
    int AccentPosition,
    IReadOnlyList<string> Morae,
    IReadOnlyList<bool> HighMorae,
    string Notation);

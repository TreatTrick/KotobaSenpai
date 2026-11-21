using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.WordGrouping;

public sealed class SpanPitchResolver : ISpanPitchResolver
{
    private readonly IPitchAccentDictionary _dictionary;
    private readonly IReadOnlyDictionary<(string Surface, string Reading), IReadOnlyList<int>> _overrides;

    public SpanPitchResolver(IPitchAccentDictionary dictionary) : this(dictionary, ManualPitchOverrides.Entries) { }

    internal SpanPitchResolver(IPitchAccentDictionary dictionary,
        IReadOnlyDictionary<(string Surface, string Reading), IReadOnlyList<int>> overrides)
    {
        _dictionary = dictionary;
        _overrides = overrides;
    }

    public IReadOnlyList<SpanPitch?> Resolve(IReadOnlyList<GroupedWord> words)
    {
        var results = new SpanPitch?[words.Count];
        var requests = new HashSet<(string Surface, string Reading)>();
        var pending = new List<(int Index, string Surface, string Normalized, string Reading)>();
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var reading = Kana.ToHiragana(word.Reading);
            if (!JapaneseText.IsHiraganaReading(reading))
                continue;

            var normalized = JapaneseNumerals.Normalize(word.Surface);
            var manual = Find(_overrides, word.Surface, reading, SpanPitchSource.ManualOverride)
                ?? (word.Surface != normalized ? Find(_overrides, normalized, reading, SpanPitchSource.ManualOverride) : null);
            if (manual is not null)
            {
                results[index] = manual;
                continue;
            }
            requests.Add((word.Surface, reading));
            requests.Add((normalized, reading));
            pending.Add((index, word.Surface, normalized, reading));
        }

        if (requests.Count == 0)
            return results;

        var entries = _dictionary.FindByKeys(requests);
        foreach (var (index, surface, normalized, reading) in pending)
        {
            results[index] = Find(entries, surface, reading, SpanPitchSource.Dictionary)
                ?? (surface != normalized ? Find(entries, normalized, reading, SpanPitchSource.Dictionary) : null);
            if (results[index] is null && WhitelistedCounterPitchRules.ResolvePosition(surface, reading) is int position
                && position <= PitchAccent.SplitMorae(reading).Count)
                results[index] = new SpanPitch(reading, [position], SpanPitchSource.CounterRule);
        }
        return results;
    }

    private static SpanPitch? Find(
        IReadOnlyDictionary<(string Surface, string Reading), IReadOnlyList<int>> entries,
        string surface, string reading, SpanPitchSource source)
    {
        if (!entries.TryGetValue((surface, reading), out var candidates))
            return null;
        var moraCount = PitchAccent.SplitMorae(reading).Count;
        var positions = candidates.Where(position => position >= 0 && position <= moraCount).Distinct().ToArray();
        return positions.Length == 0 ? null : new SpanPitch(reading, positions, source);
    }
}

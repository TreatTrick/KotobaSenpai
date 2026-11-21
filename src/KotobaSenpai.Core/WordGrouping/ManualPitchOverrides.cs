using System.Collections.Frozen;

namespace KotobaSenpai.Core.WordGrouping;

internal static class ManualPitchOverrides
{
    // Add exact keys only when a correction has an independently verified source.
    internal static readonly FrozenDictionary<(string Surface, string Reading), IReadOnlyList<int>> Entries
        = FrozenDictionary<(string Surface, string Reading), IReadOnlyList<int>>.Empty;
}

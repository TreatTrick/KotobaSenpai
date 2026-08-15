using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Hover-overlap tie-breaking: when the cursor hits multiple phrase groups, pick the group with fewer
/// referenced tokens; on a tie pick the one earlier in the provider return order. Returns the index of the hit
/// group, or -1 when nothing is hit.
/// </summary>
public static class PhraseHoverResolver
{
    public static int Resolve(IReadOnlyList<PhraseGroupView> groups, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(groups);

        int best = -1;
        foreach (var (group, index) in groups.Select((group, index) => (group, index)))
        {
            if (!group.Parts.Any(part => part.Rects.Any(rect => Contains(rect, x, y))))
                continue;
            if (best < 0
                || group.DistinctTokenCount < groups[best].DistinctTokenCount
                || (group.DistinctTokenCount == groups[best].DistinctTokenCount && group.ProviderOrder < groups[best].ProviderOrder))
            {
                best = index;
            }
        }
        return best;
    }

    private static bool Contains(ScreenRect rect, int x, int y)
        => x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;
}
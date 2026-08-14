using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 悬停重叠决胜：光标命中多个 phrase group 时，选引用 token 总数更少的 group；相同则选
/// provider 返回顺序更靠前的。返回命中 group 的下标，未命中返回 -1。
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
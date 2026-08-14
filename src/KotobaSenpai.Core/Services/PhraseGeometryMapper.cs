using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 把已验证 phrase group 的 token 引用映射为逐 part、逐行的可绘制矩形。每个 part 只覆盖其引用 token
/// 的字符框并按行拆分，绝不把 group 间隔 token 或空白区域并入几何。
/// </summary>
public static class PhraseGeometryMapper
{
    public static PhrasePartView MapPart(PhraseGroupPart part)
    {
        var rects = part.Tokens
            .SelectMany(token => token.Boxes.Select(box => (LineId: token.LineId, Box: box)))
            .GroupBy(item => item.LineId)
            .Select(group => Union(group.Select(item => item.Box)))
            .ToArray();
        return new PhrasePartView(rects);
    }

    public static PhraseGroupView MapGroup(PhraseGroup group)
        => new(
            group.SessionGroupId,
            group.Label,
            group.Type,
            group.MeaningZh,
            group.GrammarZh,
            group.ProviderOrder,
            group.DistinctTokenCount,
            group.Parts.Select(MapPart).ToArray());

    private static ScreenRect Union(IEnumerable<ScreenRect> boxes)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
        foreach (var box in boxes)
        {
            x1 = Math.Min(x1, box.X);
            y1 = Math.Min(y1, box.Y);
            x2 = Math.Max(x2, box.Right);
            y2 = Math.Max(y2, box.Bottom);
        }
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }
}
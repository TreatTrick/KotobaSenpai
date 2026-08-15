using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Maps a validated phrase group's token references to drawable rectangles per part and per line. Each part
/// covers only the character boxes of its referenced tokens and splits by line, never merging group-gap
/// tokens or blank regions into the geometry.
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
            group.Meaning,
            group.Grammar,
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
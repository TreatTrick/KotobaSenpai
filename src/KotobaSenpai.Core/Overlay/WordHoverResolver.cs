using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Core.Overlay;

/// <summary>Chooses the nearest word center among words whose source rectangles contain the cursor.</summary>
public static class WordHoverResolver
{
    public static int Resolve(IReadOnlyList<GroupedWord> words, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(words);

        // Local-word hot zone: hit any rect of a word; on overlap, the one whose union center is nearest.
        int hit = -1;
        double best = double.MaxValue;
        for (int i = 0; i < words.Count; i++)
        {
            if (!words[i].Rects.Any(rect => Contains(rect, cursorX, cursorY)))
                continue;
            var b = words[i].Bounds;
            var cx = b.X + b.Width / 2d;
            var cy = b.Y + b.Height / 2d;
            var d = (cursorX - cx) * (cursorX - cx) + (cursorY - cy) * (cursorY - cy);
            if (d < best)
            {
                best = d;
                hit = i;
            }
        }

        return hit;
    }

    private static bool Contains(ScreenRect rect, int x, int y)
        => x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;
}

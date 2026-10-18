using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Platform.Windows.Overlay;

internal readonly record struct FuriganaLinePart(ScreenRect Bounds, int MoraStart, int MoraCount);

internal static class FuriganaLayout
{
    internal static IReadOnlyList<FuriganaLinePart> GetParts(
        GroupedWord word, int sourceTokenIndex, int surfaceOffset, int surfaceLength, int moraCount)
    {
        if (surfaceLength <= 0 || moraCount <= 0 || sourceTokenIndex < 0 || sourceTokenIndex >= word.SourceTokenLines.Count)
            return [];
        var end = surfaceOffset + surfaceLength;
        var parts = new List<FuriganaLinePart>();
        foreach (var line in word.SourceTokenLines[sourceTokenIndex].OrderBy(line => line.SurfaceOffset))
        {
            var start = Math.Max(surfaceOffset, line.SurfaceOffset);
            var finish = Math.Min(end, line.SurfaceOffset + line.SurfaceLength);
            if (finish <= start)
                continue;
            // ponytail: proportional whole-mora allocation is a layout fallback; exact per-kanji readings need aligned dictionary data.
            var moraStart = (int)Math.Round((start - surfaceOffset) * (double)moraCount / surfaceLength, MidpointRounding.AwayFromZero);
            var moraEnd = (int)Math.Round((finish - surfaceOffset) * (double)moraCount / surfaceLength, MidpointRounding.AwayFromZero);
            if (moraEnd <= moraStart)
                continue;
            var left = line.Bounds.X + line.Bounds.Width * (start - line.SurfaceOffset) / line.SurfaceLength;
            var right = line.Bounds.X + line.Bounds.Width * (finish - line.SurfaceOffset) / line.SurfaceLength;
            parts.Add(new FuriganaLinePart(
                new ScreenRect(left, line.Bounds.Y, Math.Max(1, right - left), line.Bounds.Height),
                moraStart, moraEnd - moraStart));
        }
        return parts;
    }
}

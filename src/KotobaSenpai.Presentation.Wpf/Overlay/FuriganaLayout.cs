using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

internal readonly record struct FuriganaLinePart(ScreenRect Bounds, int MoraStart, int MoraCount);

internal static class FuriganaLayout
{
    internal static IReadOnlyList<FuriganaLinePart> GetParts(
        GroupedWord word, int sourceTokenIndex, int surfaceOffset, int surfaceLength, int moraCount)
    {
        if (surfaceLength <= 0 || moraCount <= 0 || sourceTokenIndex < 0 || sourceTokenIndex >= word.SourceTokenLines.Count)
            return [];
        return GetParts(word.SourceTokenLines[sourceTokenIndex].OrderBy(line => line.SurfaceOffset),
            surfaceOffset, surfaceLength, moraCount, mergeByLine: false);
    }

    internal static IReadOnlyList<FuriganaLinePart> GetSpanParts(
        GroupedWord word, int surfaceOffset, int surfaceLength, int moraCount)
    {
        if (surfaceLength <= 0 || moraCount <= 0)
            return [];
        return GetParts(FlattenSourceTokenLines(word), surfaceOffset, surfaceLength, moraCount, mergeByLine: true);
    }

    private static IEnumerable<SourceTokenLineGeometry> FlattenSourceTokenLines(GroupedWord word)
    {
        var tokenOffset = 0;
        for (var i = 0; i < word.SourceTokens.Count; i++)
        {
            foreach (var line in word.SourceTokenLines[i])
                yield return line with { SurfaceOffset = tokenOffset + line.SurfaceOffset };
            tokenOffset += word.SourceTokens[i].Surface.Length;
        }
    }

    private static IReadOnlyList<FuriganaLinePart> GetParts(
        IEnumerable<SourceTokenLineGeometry> lines, int surfaceOffset, int surfaceLength, int moraCount, bool mergeByLine)
    {
        var slices = new List<SourceTokenLineGeometry>();
        foreach (var line in lines)
        {
            var start = Math.Max(surfaceOffset, line.SurfaceOffset);
            var finish = Math.Min(surfaceOffset + surfaceLength, line.SurfaceOffset + line.SurfaceLength);
            if (finish <= start)
                continue;
            var left = line.Bounds.X + line.Bounds.Width * (start - line.SurfaceOffset) / line.SurfaceLength;
            var right = line.Bounds.X + line.Bounds.Width * (finish - line.SurfaceOffset) / line.SurfaceLength;
            slices.Add(new SourceTokenLineGeometry(line.LineIndex, start, finish - start,
                new ScreenRect(left, line.Bounds.Y, Math.Max(1, right - left), line.Bounds.Height)));
        }

        var parts = new List<FuriganaLinePart>();
        foreach (var slice in mergeByLine ? MergeByLine(slices) : slices)
        {
            // Apportion whole morae by surface offset when no per-character reading alignment is available.
            var moraStart = (int)Math.Round((slice.SurfaceOffset - surfaceOffset) * (double)moraCount / surfaceLength,
                MidpointRounding.AwayFromZero);
            var moraEnd = (int)Math.Round((slice.SurfaceOffset + slice.SurfaceLength - surfaceOffset) * (double)moraCount / surfaceLength,
                MidpointRounding.AwayFromZero);
            if (moraEnd > moraStart)
                parts.Add(new FuriganaLinePart(slice.Bounds, moraStart, moraEnd - moraStart));
        }
        return parts;
    }

    private static IEnumerable<SourceTokenLineGeometry> MergeByLine(IEnumerable<SourceTokenLineGeometry> slices)
    {
        foreach (var line in slices.GroupBy(slice => slice.LineIndex).OrderBy(line => line.Key))
        {
            var start = line.Min(slice => slice.SurfaceOffset);
            var end = line.Max(slice => slice.SurfaceOffset + slice.SurfaceLength);
            var left = line.Min(slice => slice.Bounds.X);
            var top = line.Min(slice => slice.Bounds.Y);
            var right = line.Max(slice => slice.Bounds.Right);
            var bottom = line.Max(slice => slice.Bounds.Bottom);
            yield return new SourceTokenLineGeometry(line.Key, start, end - start,
                new ScreenRect(left, top, right - left, bottom - top));
        }
    }
}

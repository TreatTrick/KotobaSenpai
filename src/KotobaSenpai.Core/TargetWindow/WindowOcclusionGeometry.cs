using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Core.TargetWindow;

/// <summary>Evaluates the union of occluders clipped to the target client area.</summary>
public static class WindowOcclusionGeometry
{
    public const double OcclusionHideThreshold = 0.30;

    public static bool HasPositiveAreaIntersection(ScreenRect first, ScreenRect second)
        => first.X < second.Right
            && first.Right > second.X
            && first.Y < second.Bottom
            && first.Bottom > second.Y;

    public static bool ExceedsOcclusionThreshold(ScreenRect targetBounds, IEnumerable<ScreenRect> occluders, out double occlusionRatio)
    {
        var clipped = occluders.Where(bounds => HasPositiveAreaIntersection(targetBounds, bounds))
            .Select(bounds => bounds.ClampTo(targetBounds)).ToArray();
        var edges = clipped.SelectMany(bounds => new[] { bounds.X, bounds.Right }).Distinct().Order().ToArray();
        long coveredArea = 0;
        // ponytail: O(n^2 log n) for a small overlapping window set; use a region index only if profiling shows a bottleneck.
        for (var i = 1; i < edges.Length; i++)
        {
            long coveredHeight = 0;
            var bottom = int.MinValue;
            foreach (var bounds in clipped.Where(bounds => bounds.X < edges[i] && bounds.Right > edges[i - 1]).OrderBy(bounds => bounds.Y))
            {
                var top = Math.Max(bounds.Y, bottom);
                if (bounds.Bottom > top)
                    coveredHeight += (long)bounds.Bottom - top;
                bottom = Math.Max(bottom, bounds.Bottom);
            }
            coveredArea += (long)(edges[i] - edges[i - 1]) * coveredHeight;
        }
        occlusionRatio = coveredArea / ((double)targetBounds.Width * targetBounds.Height);
        return occlusionRatio > OcclusionHideThreshold;
    }
}

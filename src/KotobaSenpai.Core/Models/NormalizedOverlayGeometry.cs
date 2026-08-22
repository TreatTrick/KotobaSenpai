namespace KotobaSenpai.Core.Models;

/// <summary>Stable target-relative geometry for one recognized word, including one rect per line.</summary>
public sealed class NormalizedWordGeometry
{
    public NormalizedWordGeometry(IEnumerable<NormalizedRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);
        Rects = rects.ToArray();
    }

    public IReadOnlyList<NormalizedRect> Rects { get; }

    public IReadOnlyList<ScreenRect> ToScreen(ScreenRect targetBounds)
        => Rects.Select(rect => rect.ToScreen(targetBounds)).ToArray();
}

/// <summary>Stable target-relative geometry for one phrase part, split into its drawable line rects.</summary>
public sealed class NormalizedPhrasePartGeometry
{
    public NormalizedPhrasePartGeometry(IEnumerable<NormalizedRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);
        Rects = rects.ToArray();
    }

    public IReadOnlyList<NormalizedRect> Rects { get; }

    public IReadOnlyList<ScreenRect> ToScreen(ScreenRect targetBounds)
        => Rects.Select(rect => rect.ToScreen(targetBounds)).ToArray();
}

/// <summary>Stable target-relative geometry for all parts in one phrase group.</summary>
public sealed class NormalizedPhraseGroupGeometry
{
    public NormalizedPhraseGroupGeometry(IEnumerable<NormalizedPhrasePartGeometry> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Parts = parts.ToArray();
    }

    public IReadOnlyList<NormalizedPhrasePartGeometry> Parts { get; }
}

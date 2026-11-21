using KotobaSenpai.Core.Tokenization;
namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>
/// Overlay-visible word meaning with the effective reading and the identity of its local span when available.
/// </summary>
public sealed record WordMeaningView(
    string Headword,
    string Reading,
    string Pos,
    string Meaning,
    string Grammar,
    IReadOnlyList<PitchAccentSummary>? PitchAccents = null)
{
    public string? SegmentId { get; init; }
    public int? SpanIndex { get; init; }
    public bool HasOverriddenReading { get; init; }

    public string PitchAccentText
        => HasOverriddenReading ? string.Empty : string.Join(" | ", (PitchAccents ?? Array.Empty<PitchAccentSummary>())
            .Where(pitch => !string.IsNullOrEmpty(pitch.Notation))
            .Select(pitch => pitch.Notation));

}

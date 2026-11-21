using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Japanese;
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
    public SpanPitch? SpanPitch { get; init; }
    public WordPitchReference? WordPitch { get; init; }
    public IReadOnlyList<PitchAccentSummary> CorrectedPitchAccents { get; init; } = Array.Empty<PitchAccentSummary>();

    public string PitchAccentText
        => SpanPitch is { } pitch ? string.Join(" / ", pitch.Positions
            .Select(position => PitchAccent.CreatePattern(pitch.Reading, position)?.Notation)
            .OfType<string>())
            : HasOverriddenReading
                ? string.Join(" | ", CorrectedPitchAccents
                    .Where(pitch => !string.IsNullOrEmpty(pitch.Notation))
                    .Select(pitch => pitch.Notation))
                : string.Join(" | ", (PitchAccents ?? Array.Empty<PitchAccentSummary>())
                    .Where(pitch => !string.IsNullOrEmpty(pitch.Notation))
                    .Select(pitch => pitch.Notation));

}

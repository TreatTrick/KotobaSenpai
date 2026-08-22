using KotobaSenpai.Core.Tokenization;
namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>
/// Overlay-visible word meaning: the local merged word's headword/reading (from the request's local span) plus the
/// provider's contextual pos/meaning/grammar. Matched by its merged surface+reading.
/// </summary>
public sealed record WordMeaningView(
    string Headword,
    string Reading,
    string Pos,
    string Meaning,
    string Grammar,
    IReadOnlyList<PitchAccentSummary>? PitchAccents = null)
{
    public string PitchAccentText
        => string.Join(" | ", (PitchAccents ?? Array.Empty<PitchAccentSummary>())
            .Where(pitch => !string.IsNullOrEmpty(pitch.Notation))
            .Select(pitch => pitch.Notation));

}

namespace KotobaSenpai.Core.Models;

/// <summary>
/// Overlay-visible word meaning: the local merged word's headword/reading (from the request's local span) plus the
/// provider's contextual pos/meaning/grammar. Matched back to a <see cref="GroupedWord"/> by its merged surface+reading.
/// </summary>
public sealed record WordMeaningView(
    string Headword,
    string Reading,
    string Pos,
    string Meaning,
    string Grammar);
namespace KotobaSenpai.Core.Models;

/// <summary>
/// The orchestrator's result for a phrase analysis run of one recognition: whether it succeeded, the
/// validated group views with assigned session ids (in frame coordinates), the validated per-word meanings, and a
/// retryable warning. When it failed or all groups were invalid, Groups is empty and local words/spans are unaffected.
/// </summary>
public sealed record PhraseAnalysisRun(PhraseAnalysisOutcome Outcome, IReadOnlyList<PhraseGroupView> Groups, string? Warning = null)
{
    public bool Succeeded => Outcome == PhraseAnalysisOutcome.Success;

    /// <summary>Sentence segments whose provider response completed successfully, including empty valid responses.</summary>
    public IReadOnlySet<string> SuccessfulSegmentIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Validated per-word meanings (referencing local merged spans by surface+reading); empty when none or analysis failed.</summary>
    public IReadOnlyList<WordMeaningView> Words { get; init; } = Array.Empty<WordMeaningView>();
}

/// <summary>Overlay-visible phrase group view: a shared session id, display fields, provider order, and per-part geometry.</summary>
public sealed record PhraseGroupView(
    Guid SessionGroupId,
    string Label,
    string Type,
    string Meaning,
    string Grammar,
    int ProviderOrder,
    int DistinctTokenCount,
    IReadOnlyList<PhrasePartView> Parts);

/// <summary>A part's drawable geometry: split into multiple rectangles by line, never spanning blank regions.</summary>
public sealed record PhrasePartView(IReadOnlyList<ScreenRect> Rects);

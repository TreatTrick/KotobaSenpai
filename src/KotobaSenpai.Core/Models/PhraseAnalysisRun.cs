namespace KotobaSenpai.Core.Models;

/// <summary>
/// The orchestrator's result for a phrase analysis run of one recognition: whether it succeeded, the
/// validated group views with assigned session ids (in frame coordinates), and a retryable warning. When it
/// failed or all groups were invalid, Groups is empty and local words/spans are unaffected.
/// </summary>
public sealed record PhraseAnalysisRun(PhraseAnalysisOutcome Outcome, IReadOnlyList<PhraseGroupView> Groups, string? Warning = null)
{
    public bool Succeeded => Outcome == PhraseAnalysisOutcome.Success;
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
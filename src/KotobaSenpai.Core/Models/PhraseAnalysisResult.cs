namespace KotobaSenpai.Core.Models;

/// <summary>Phrase analysis result: whether the provider succeeded, the unvalidated line-level groups, and an optional diagnostic warning.</summary>
public sealed record PhraseAnalysisResult(
    PhraseAnalysisOutcome Outcome,
    IReadOnlyList<ParsedPhraseGroup> Groups,
    string? Warning = null)
{
    public bool Succeeded => Outcome == PhraseAnalysisOutcome.Success;
}

/// <summary>Phrase analysis outcome category. On failure no partial group is rendered and local words/spans remain usable.</summary>
public enum PhraseAnalysisOutcome
{
    Success,
    NoKey,
    Timeout,
    Cancelled,
    Refused,
    MalformedJson,
    TransportError,
    InvalidResponse,
}
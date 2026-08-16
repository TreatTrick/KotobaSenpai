namespace KotobaSenpai.Core.Models;

/// <summary>Phrase analysis result: whether the provider succeeded, the unvalidated line-level groups and word meanings, and an optional diagnostic warning.</summary>
public sealed record PhraseAnalysisResult
{
    public PhraseAnalysisResult(
        PhraseAnalysisOutcome outcome,
        IReadOnlyList<ParsedPhraseGroup> groups,
        string? warning = null)
    {
        Outcome = outcome;
        Groups = groups;
        Warning = warning;
    }

    public PhraseAnalysisOutcome Outcome { get; }

    public IReadOnlyList<ParsedPhraseGroup> Groups { get; }

    public string? Warning { get; }

    /// <summary>Provider-returned, not-yet-validated per-word meanings (referencing local merged spans).</summary>
    public IReadOnlyList<ParsedWordMeaning> Words { get; set; } = Array.Empty<ParsedWordMeaning>();

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
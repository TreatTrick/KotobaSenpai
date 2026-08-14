namespace KotobaSenpai.Core.Models;

/// <summary>phrase 分析结果：提供方是否成功、未校验的线级 group，以及可选的诊断警告。</summary>
public sealed record PhraseAnalysisResult(
    PhraseAnalysisOutcome Outcome,
    IReadOnlyList<ParsedPhraseGroup> Groups,
    string? Warning = null)
{
    public bool Succeeded => Outcome == PhraseAnalysisOutcome.Success;
}

/// <summary>phrase 分析结果类别。失败时不渲染任何部分 group，本地词/span 保持可用。</summary>
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
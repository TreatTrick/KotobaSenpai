namespace KotobaSenpai.Core.Models;

/// <summary>本地已解析的连续 JMdict span 摘要，供 LLM 看到已由本地托底的连续词，避免其重复发现。</summary>
public sealed record LocalSpanSummary(
    string Surface,
    string Reading,
    string LookupKey,
    IReadOnlyList<SentenceTokenId> TokenIds);
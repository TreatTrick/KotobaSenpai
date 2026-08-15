namespace KotobaSenpai.Core.Models;

/// <summary>Summary of a locally resolved contiguous JMdict span, letting the LLM see the contiguous words already covered locally and avoid re-discovering them.</summary>
public sealed record LocalSpanSummary(
    string Surface,
    string Reading,
    string LookupKey,
    IReadOnlyList<SentenceTokenId> TokenIds);
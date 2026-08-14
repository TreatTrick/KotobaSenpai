namespace KotobaSenpai.Core.Models;

/// <summary>
/// 一次 LLM phrase 分析请求：单个句级 segment 的文本、稳定 token 引用与本地连续 span 摘要。
/// 只携带文本与元数据，绝不包含截图、窗口坐标、窗口标题或 API key。
/// </summary>
public sealed record PhraseAnalysisRequest
{
    public PhraseAnalysisRequest(
        string segmentId,
        string segmentText,
        IReadOnlyList<SentenceTokenReference> tokens,
        IReadOnlyList<LocalSpanSummary> localSpans)
    {
        if (string.IsNullOrWhiteSpace(segmentId))
            throw new ArgumentException("Segment id must not be empty.", nameof(segmentId));
        ArgumentNullException.ThrowIfNull(segmentText);
        tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        localSpans = localSpans ?? throw new ArgumentNullException(nameof(localSpans));

        SegmentId = segmentId;
        SegmentText = segmentText;
        Tokens = tokens.ToArray();
        LocalSpans = localSpans.ToArray();
    }

    /// <summary>请求内句段 ID（仅请求内唯一）。</summary>
    public string SegmentId { get; }

    /// <summary>句段原始 OCR 文本。</summary>
    public string SegmentText { get; }

    /// <summary>句段内按阅读顺序的稳定 token 引用。</summary>
    public IReadOnlyList<SentenceTokenReference> Tokens { get; }

    /// <summary>本地已解析的连续 span 摘要。</summary>
    public IReadOnlyList<LocalSpanSummary> LocalSpans { get; }
}
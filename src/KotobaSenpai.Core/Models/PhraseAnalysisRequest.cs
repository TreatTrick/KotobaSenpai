namespace KotobaSenpai.Core.Models;

/// <summary>
/// A single LLM phrase analysis request: the text of one sentence-level segment, stable token references, and
/// local contiguous span summaries. It carries only text and metadata, never screenshots, window coordinates,
/// window titles, or API keys.
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

    /// <summary>Request-scoped segment id (unique only within the request).</summary>
    public string SegmentId { get; }

    /// <summary>The segment's raw OCR text.</summary>
    public string SegmentText { get; }

    /// <summary>The segment's stable token references in reading order.</summary>
    public IReadOnlyList<SentenceTokenReference> Tokens { get; }

    /// <summary>Locally resolved contiguous span summaries.</summary>
    public IReadOnlyList<LocalSpanSummary> LocalSpans { get; }
}
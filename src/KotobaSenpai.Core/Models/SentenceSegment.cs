namespace KotobaSenpai.Core.Models;

/// <summary>A sentence-level segment: a sequence of OCR line indices in a contiguous, reliable reading order. Cross-segment token references are forbidden.</summary>
public sealed record SentenceSegment(IReadOnlyList<int> LineIndices)
{
    public SentenceSegment()
        : this(Array.Empty<int>())
    {
    }

    public string SegmentId => $"s{LineIndices[0]}-{LineIndices[^1]}";
}
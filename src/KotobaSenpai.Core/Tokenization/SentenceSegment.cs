namespace KotobaSenpai.Core.Tokenization;

/// <summary>An analysis block: a sequence of prepared OCR line indices in a contiguous, reliable reading order. Cross-block token references are forbidden.</summary>
public sealed record SentenceSegment(IReadOnlyList<int> LineIndices)
{
    public SentenceSegment()
        : this(Array.Empty<int>())
    {
    }

    public string SegmentId => $"s{LineIndices[0]}-{LineIndices[^1]}";
}

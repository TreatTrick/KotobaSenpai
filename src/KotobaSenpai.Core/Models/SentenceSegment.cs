namespace KotobaSenpai.Core.Models;

/// <summary>一个句级 segment：连续、可靠阅读顺序的 OCR 行索引序列。跨 segment 的 token 引用被禁止。</summary>
public sealed record SentenceSegment(IReadOnlyList<int> LineIndices)
{
    public SentenceSegment()
        : this(Array.Empty<int>())
    {
    }

    public string SegmentId => $"s{LineIndices[0]}-{LineIndices[^1]}";
}
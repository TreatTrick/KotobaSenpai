using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 把 OCR 行按句段划分，保留阅读顺序。相邻行仅在顺序/布局可靠、无句末标点、无段落间隙时合并；
/// 跨 segment 的 token 引用被禁止，避免把不同对话块拼在一起。
/// </summary>
public sealed class SentenceSegmenter
{
    private static readonly string SentenceFinalPunctuation = "。！？…‥．";

    /// <summary>视为段落间隙的垂直间距因子（相对上一行行高）。</summary>
    private const double ParagraphGapFactor = 1.5;

    public IReadOnlyList<SentenceSegment> Segment(IReadOnlyList<OcrLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var segments = new List<SentenceSegment>();
        var current = new List<int>();
        var previousBounds = default(LineBounds);

        for (int i = 0; i < lines.Count; i++)
        {
            var bounds = ComputeBounds(lines[i]);
            if (current.Count > 0 && ShouldBreak(previousBounds, lines[i - 1], bounds))
            {
                segments.Add(new SentenceSegment(current.ToArray()));
                current = new List<int>();
            }
            current.Add(i);
            previousBounds = bounds;
        }

        if (current.Count > 0)
            segments.Add(new SentenceSegment(current.ToArray()));
        return segments;
    }

    private static bool ShouldBreak(LineBounds previous, OcrLine previousLine, LineBounds next)
    {
        if (previousLine.Words.Count == 0 || IsSentenceFinal(previousLine.Text))
            return true;
        var gap = next.Top - previous.Bottom;
        if (gap > Math.Max(1, previous.Height) * ParagraphGapFactor)
            return true;
        // 下一行起点左移说明阅读顺序不可靠（假定从左到右），切段。
        return next.Left < previous.Left;
    }

    private static bool IsSentenceFinal(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
            return true;
        var last = trimmed[^1];
        return SentenceFinalPunctuation.Contains(last);
    }

    private static LineBounds ComputeBounds(OcrLine line)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
        foreach (var word in line.Words)
        {
            var b = word.FrameBounds;
            x1 = Math.Min(x1, b.X);
            y1 = Math.Min(y1, b.Y);
            x2 = Math.Max(x2, b.Right);
            y2 = Math.Max(y2, b.Bottom);
        }
        return new LineBounds(x1, y1, x2, y2);
    }

    private readonly record struct LineBounds(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}
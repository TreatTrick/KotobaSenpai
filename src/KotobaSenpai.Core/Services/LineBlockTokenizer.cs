using System.Text;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Tokenizes a block of OCR lines as one merged text so a word split across lines becomes a single token, then maps each
/// token's offset back to the original lines' character boxes. A cross-line token therefore carries one <see cref="LineSegment"/>
/// per line it spans. Shared by the overlay grouping and the LLM request builder so both see the same word boundaries.
/// </summary>
public static class LineBlockTokenizer
{
    /// <summary>One token and the character boxes it covers, split per line (a cross-line token has &gt;1 segment).</summary>
    public sealed record SplitToken(Token Token, IReadOnlyList<LineSegment> Segments);

    /// <summary>The boxes a token covers on a single line (the line's union of those chars).</summary>
    public sealed record LineSegment(int LineIndex, IReadOnlyList<ScreenRect> Boxes);

    public static IReadOnlyList<SplitToken> Tokenize(ITokenizer tokenizer, IReadOnlyList<OcrLine> lines)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(lines);

        // Concatenate the lines into one text (no separator, so the tokenizer sees continuous text). Track each line's
        // start offset so a token's merged-text offset can be split back across lines.
        var merged = new StringBuilder();
        var lineStarts = new int[lines.Count + 1];
        for (int i = 0; i < lines.Count; i++)
        {
            lineStarts[i] = merged.Length;
            merged.Append(lines[i].Text);
        }
        lineStarts[lines.Count] = merged.Length;

        var result = new List<SplitToken>();
        foreach (var token in tokenizer.Tokenize(merged.ToString()))
        {
            int start = token.StartOffset, end = start + token.Surface.Length;
            var segments = new List<LineSegment>();
            for (int li = 0; li < lines.Count && start < end; li++)
            {
                int localStart = Math.Max(start, lineStarts[li]) - lineStarts[li];
                int localEnd = Math.Min(end, lineStarts[li + 1]) - lineStarts[li];
                if (localStart >= localEnd)
                    continue;
                var boxes = lines[li].Words.Skip(localStart).Take(localEnd - localStart)
                    .Select(word => word.FrameBounds).ToArray();
                if (boxes.Length > 0)
                    segments.Add(new LineSegment(li, boxes));
            }
            if (segments.Count > 0)
                result.Add(new SplitToken(token, segments));
        }
        return result;
    }

    /// <summary>Returns one rect per line (the union of the subset tokens' boxes on that line), for a span's tokens.</summary>
    public static IReadOnlyList<ScreenRect> RectsForTokens(
        IReadOnlyList<SplitToken> splitTokens,
        IEnumerable<Token> tokenSubset)
    {
        var byToken = splitTokens.ToDictionary(st => st.Token);
        var byLine = tokenSubset
            .SelectMany(token => byToken[token].Segments)
            .GroupBy(segment => segment.LineIndex)
            .OrderBy(group => group.Key);
        return byLine
            .Select(group => Union(group.SelectMany(segment => segment.Boxes)))
            .ToArray();
    }

    private static ScreenRect Union(IEnumerable<ScreenRect> boxes)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
        foreach (var b in boxes)
        {
            x1 = Math.Min(x1, b.X);
            y1 = Math.Min(y1, b.Y);
            x2 = Math.Max(x2, b.Right);
            y2 = Math.Max(y2, b.Bottom);
        }
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }
}
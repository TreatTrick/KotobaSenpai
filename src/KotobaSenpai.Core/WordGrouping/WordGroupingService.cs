using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Core.WordGrouping;

/// <summary>
/// Groups OCR characters into words by tokenizing each sentence segment (a set of merged lines) as one text block, so a
/// word split across lines becomes a single word with one rect per line. Produces one underline geometry per word rect.
/// Word boundaries and geometry are resolved entirely by the injected <see cref="ITokenSpanResolver"/>, which is
/// responsible for punctuation/whitespace filtering; tokens with no character box are skipped.
/// </summary>
public sealed class WordGroupingService : IOcrWordGroupingService
{
    private readonly ITokenizer _tokenizer;
    private readonly SentenceSegmenter _segmenter;
    private readonly ITokenSpanResolver _spanResolver;

    public WordGroupingService(ITokenizer tokenizer, SentenceSegmenter segmenter, ITokenSpanResolver spanResolver)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
        _spanResolver = spanResolver ?? throw new ArgumentNullException(nameof(spanResolver));
    }

    public IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var result = new List<GroupedWord>();
        foreach (var segment in _segmenter.Segment(lines))
        {
            var block = segment.LineIndices.Select(i => lines[i]).ToArray(); // Include every OCR line in the segment.
            var splitTokens = LineBlockTokenizer.Tokenize(_tokenizer, block);

            var tokens = splitTokens.Select(st => st.Token).ToArray();
            var byToken = splitTokens.ToDictionary(st => st.Token);
            foreach (var span in _spanResolver.Resolve(tokens))
            {
                var rects = LineBlockTokenizer.RectsForTokens(splitTokens, span.Tokens);
                if (rects.Count == 0)
                    continue;
                var sourceTokenLines = span.Tokens
                    .Select(token => byToken[token].Segments
                        .Select(line => new SourceTokenLineGeometry(
                            line.LineIndex,
                            line.SurfaceOffset,
                            line.SurfaceLength,
                            Union(line.Boxes)))
                        .ToArray())
                    .ToArray();
                result.Add(new GroupedWord(span, rects, sourceTokenLines).WithSegmentId(segment.SegmentId));
            }
        }
        return result;
    }

    private static ScreenRect Union(IEnumerable<ScreenRect> boxes)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
        foreach (var box in boxes)
        {
            x1 = Math.Min(x1, box.X);
            y1 = Math.Min(y1, box.Y);
            x2 = Math.Max(x2, box.Right);
            y2 = Math.Max(y2, box.Bottom);
        }
        return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
    }
}

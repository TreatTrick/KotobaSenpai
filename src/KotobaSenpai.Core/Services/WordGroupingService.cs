using System.Globalization;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Groups OCR characters into words by tokenizing each sentence segment (a set of merged lines) as one text block, so a
/// word split across lines becomes a single word with one rect per line. Produces one underline geometry per word rect.
/// Scope: all words including particles, only punctuation and whitespace tokens are excluded; tokens with no character
/// box are skipped.
/// </summary>
public sealed class WordGroupingService : IOcrWordGroupingService
{
    private readonly ITokenizer _tokenizer;
    private readonly ITokenSpanResolver? _spanResolver;
    private readonly SentenceSegmenter? _segmenter;

    public WordGroupingService(ITokenizer tokenizer, ITokenSpanResolver? spanResolver = null, SentenceSegmenter? segmenter = null)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _spanResolver = spanResolver;
        _segmenter = segmenter;
    }

    public IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // When no segmenter is injected, fall back to one segment per line (legacy behavior).
        var segments = _segmenter?.Segment(lines)
            ?? lines.Select((_, i) => new SentenceSegment([i])).ToArray();

        var result = new List<GroupedWord>();
        foreach (var segment in segments)
        {
            var block = segment.LineIndices.Select(i => lines[i]).ToArray();
            var splitTokens = LineBlockTokenizer.Tokenize(_tokenizer, block);

            if (_spanResolver is not null)
            {
                var tokens = splitTokens.Select(st => st.Token).ToArray();
                foreach (var span in _spanResolver.Resolve(tokens))
                {
                    var rects = LineBlockTokenizer.RectsForTokens(splitTokens, span.Tokens);
                    if (rects.Count == 0)
                        continue;
                    result.Add(new GroupedWord(span, rects));
                }
            }
            else
            {
                foreach (var st in splitTokens)
                {
                    if (IsSeparatorToken(st.Token.Surface))
                        continue;
                    var rects = st.Segments.Select(segment => Union(segment.Boxes)).ToArray();
                    result.Add(new GroupedWord(st.Token, rects));
                }
            }
        }
        return result;
    }

    private static ScreenRect Union(IReadOnlyList<ScreenRect> boxes)
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

    private static bool IsSeparatorToken(string surface)
        => surface.Length > 0 && surface.All(c => char.IsWhiteSpace(c) || IsPunctuation(c));

    private static bool IsPunctuation(char c)
        => char.GetUnicodeCategory(c) switch
        {
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation => true,
            _ => false,
        };
}
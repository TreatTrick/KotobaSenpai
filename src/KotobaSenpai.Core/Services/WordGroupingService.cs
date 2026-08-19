using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

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
            var block = segment.LineIndices.Select(i => lines[i]).ToArray();//all orclines in segment
            var splitTokens = LineBlockTokenizer.Tokenize(_tokenizer, block);

            var tokens = splitTokens.Select(st => st.Token).ToArray();
            foreach (var span in _spanResolver.Resolve(tokens))
            {
                var rects = LineBlockTokenizer.RectsForTokens(splitTokens, span.Tokens);
                if (rects.Count == 0)
                    continue;
                result.Add(new GroupedWord(span, rects));
            }
        }
        return result;
    }
}
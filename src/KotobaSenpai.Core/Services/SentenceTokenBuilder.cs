using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Tokenizes a sentence segment's merged lines as one block so a word split across lines becomes a single token, producing
/// sentence-level <see cref="SentenceTokenReference"/>s (each carrying all its line boxes) and local span summaries. Tokens
/// that cannot be mapped to a character box are skipped (no word/underline is produced, and they don't enter the request).
/// </summary>
public sealed class SentenceTokenBuilder
{
    private readonly ITokenizer _tokenizer;
    private readonly ITokenSpanResolver? _spanResolver;

    public SentenceTokenBuilder(ITokenizer tokenizer, ITokenSpanResolver? spanResolver = null)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _spanResolver = spanResolver;
    }

    public sealed record SegmentTokens(
        IReadOnlyList<SentenceTokenReference> References,
        IReadOnlyList<LocalSpanSummary> LocalSpans);

    public SegmentTokens Build(IReadOnlyList<OcrLine> lines, SentenceSegment segment)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(segment);

        var block = segment.LineIndices.Select(lineIndex => lines[lineIndex]).ToArray();
        var splitTokens = LineBlockTokenizer.Tokenize(_tokenizer, block);

        var references = new List<SentenceTokenReference>();
        // Keyed by value so a resolver returning equivalent (not identical) token instances still maps; identical duplicate
        // values in one segment are vanishingly rare and spans are contiguous, so the last-id fallback is acceptable.
        var idByToken = new Dictionary<Token, SentenceTokenId>();
        var sentenceIndex = 0;
        for (int i = 0; i < splitTokens.Count; i++)
        {
            var st = splitTokens[i];
            var boxes = st.Segments.SelectMany(s => s.Boxes).ToArray();
            if (boxes.Length == 0)
                continue; // no character box, skip
            var reference = new SentenceTokenReference(
                sentenceIndex,
                st.Segments[0].LineIndex,
                i,
                st.Token.StartOffset,
                st.Token,
                boxes);
            references.Add(reference);
            idByToken[st.Token] = reference.Id;
            sentenceIndex++;
        }

        var localSpans = new List<LocalSpanSummary>();
        if (_spanResolver is not null)
        {
            foreach (var span in _spanResolver.Resolve(splitTokens.Select(st => st.Token).ToArray()))
            {
                var spanIds = span.Tokens
                    .Where(token => idByToken.TryGetValue(token, out _))
                    .Select(token => idByToken[token])
                    .ToArray();
                if (spanIds.Length == 0)
                    continue;
                localSpans.Add(new LocalSpanSummary(span.Surface, span.Reading, span.LookupKey, spanIds, span.PitchAccents));
            }
        }

        return new SegmentTokens(references, localSpans);
    }
}

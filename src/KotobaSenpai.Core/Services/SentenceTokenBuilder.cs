using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 把句段内各行按阅读顺序分词，生成句级 <see cref="SentenceTokenReference"/>，并保留每行本地 span 摘要。
/// 映射不到字符框的 token 被跳过（不生成词/横线，也不进入请求）。
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

        var references = new List<SentenceTokenReference>();
        var localSpans = new List<LocalSpanSummary>();
        var sentenceIndex = 0;

        foreach (var lineIndex in segment.LineIndices)
        {
            var line = lines[lineIndex];
            var tokens = _tokenizer.Tokenize(line.Text);
            var lineRefs = new List<SentenceTokenReference>();

            for (int ti = 0; ti < tokens.Count; ti++)
            {
                var token = tokens[ti];
                var start = Math.Max(0, token.StartOffset);
                var end = Math.Min(line.Words.Count, start + token.Surface.Length);
                if (start >= end)
                    continue; // 无字符框，跳过

                var reference = new SentenceTokenReference(
                    sentenceIndex,
                    lineIndex,
                    ti,
                    token.StartOffset,
                    token,
                    line.Words.Skip(start).Take(end - start).Select(word => word.FrameBounds).ToArray());
                references.Add(reference);
                lineRefs.Add(reference);
                sentenceIndex++;
            }

            if (_spanResolver is not null)
            {
                foreach (var span in _spanResolver.Resolve(tokens))
                {
                    var spanIds = referenceIdsInOffsets(lineRefs, span.StartOffset, span.EndOffset);
                    if (spanIds.Count == 0)
                        continue;
                    localSpans.Add(new LocalSpanSummary(span.Surface, span.Reading, span.LookupKey, spanIds));
                }
            }
        }

        return new SegmentTokens(references, localSpans);
    }

    private static IReadOnlyList<SentenceTokenId> referenceIdsInOffsets(
        IReadOnlyList<SentenceTokenReference> refs,
        int start,
        int end)
        => refs
            .Where(reference => reference.LineOffset >= start && reference.LineOffset < end)
            .Select(reference => reference.Id)
            .ToArray();
}
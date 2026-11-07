using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>Retains completed sentence boundaries and binds their tokens to the current OCR layout.</summary>
internal sealed class SentenceBlockLayout(SentenceSegmenter segmenter, SentenceTokenBuilder tokenBuilder)
{
    private static readonly char[] OpeningDialogueQuotes = ['「', '『'];
    private static readonly char[] ClosingDialogueQuotes = ['」', '』'];
    private readonly SentenceSegmenter _segmenter = segmenter;
    private readonly SentenceTokenBuilder _tokenBuilder = tokenBuilder;

    public PhraseAnalysisPlan Prepare(Guid recognitionId, int generation, IReadOnlyList<OcrLine> lines,
        IReadOnlyList<PhraseAnalysisRequest> boundaries, string language, bool translation)
    {
        var (layout, segments) = PreserveCompletedBlocks(lines, boundaries);
        return new PhraseAnalysisPlan(recognitionId, generation, layout, segments,
            BuildRequests(recognitionId, layout, segments, language, translation));
    }

    private IReadOnlyList<PhraseAnalysisRequest> BuildRequests(Guid recognitionId, IReadOnlyList<OcrLine> lines,
        IReadOnlyList<SentenceSegment> segments, string language, bool translation)
    {
        var requests = new List<PhraseAnalysisRequest>();
        foreach (var segment in segments)
        {
            var segmentTokens = _tokenBuilder.Build(lines, segment);
            if (segmentTokens.References.Count == 0)
                continue;

            requests.Add(new PhraseAnalysisRequest(
                recognitionId,
                segment.SegmentId,
                string.Concat(segment.LineIndices.Select(i => lines[i].Text)),
                segmentTokens.References,
                segmentTokens.LocalSpans,
                language, translation));
        }
        return requests;
    }

    private (IReadOnlyList<OcrLine> Lines, IReadOnlyList<SentenceSegment> Segments) PreserveCompletedBlocks(
        IReadOnlyList<OcrLine> source, IReadOnlyList<PhraseAnalysisRequest> boundaries)
    {
        if (boundaries.Count == 0) return (source, _segmenter.Segment(source));
        var candidates = boundaries.GroupBy(request => NormalizeSegmentText(request.SegmentText))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var text = string.Concat(source.Select(line => line.Text));
        var cuts = new SortedSet<int> { 0 };
        var offset = 0;
        foreach (var line in source)
        {
            foreach (var word in line.Words)
            {
                offset += word.Text.Length;
                if ("。！？…‥．?!」』、，,.".Contains(text[offset - 1])
                    && HasCompletedBoundary(text[..offset])) cuts.Add(offset);
            }
            cuts.Add(offset);
        }
        var positions = cuts.ToArray();
        var retained = new List<(int Start, int End)>();
        for (int start = 0; start < positions.Length - 1; start++)
        {
            for (int end = positions.Length - 1; end > start; end--)
            {
                var key = NormalizeSegmentText(text[positions[start]..positions[end]]);
                if (!candidates.TryGetValue(key, out var matches)) continue;
                var fragment = SliceLines(source, positions[start], positions[end]);
                var tokens = _tokenBuilder.Build(fragment, new SentenceSegment(Enumerable.Range(0, fragment.Count).ToArray()));
                if (!matches.Any(request => HaveMatchingContentTokens(request.Tokens, tokens.References))) continue;
                retained.Add((positions[start], positions[end]));
                start = end - 1;
                break;
            }
        }
        if (retained.Count == 0) return (source, _segmenter.Segment(source));

        var layout = new List<OcrLine>();
        var segments = new List<SentenceSegment>();
        offset = 0;
        foreach (var range in retained)
        {
            AppendRange(offset, range.Start, false);
            AppendRange(range.Start, range.End, true);
            offset = range.End;
        }
        AppendRange(offset, text.Length, false);
        return (layout, segments);

        void AppendRange(int start, int end, bool completed)
        {
            if (start == end) return;
            var fragment = SliceLines(source, start, end);
            var firstLine = layout.Count;
            layout.AddRange(fragment);
            var blocks = completed ? [new SentenceSegment(Enumerable.Range(0, fragment.Count).ToArray())]
                : _segmenter.Segment(fragment);
            segments.AddRange(blocks.Select(block => new SentenceSegment(block.LineIndices.Select(index => index + firstLine).ToArray())));
        }
    }

    private static IReadOnlyList<OcrLine> SliceLines(IReadOnlyList<OcrLine> lines, int start, int end)
    {
        var result = new List<OcrLine>();
        var offset = 0;
        foreach (var line in lines)
        {
            var words = new List<OcrWord>();
            foreach (var word in line.Words)
            {
                var wordStart = offset;
                offset += word.Text.Length;
                if (wordStart >= start && offset <= end) words.Add(word);
            }
            if (words.Count > 0) result.Add(new OcrLine(words));
        }
        return result;
    }

    internal static bool HasCompletedBoundary(string text)
        => text.Length > 0 && (GetContentEnd(text) < text.Length || SentenceSegmenter.IsSentenceFinal(text));

    internal static string NormalizeSegmentText(string text)
        => text[..GetContentEnd(text)].TrimStart(OpeningDialogueQuotes);

    private static int GetContentEnd(string text)
    {
        var end = text.Length;
        // Only punctuation outside a closing dialogue quote is ignored; sentence punctuation remains significant.
        while (end > 0 && "、。，,.".Contains(text[end - 1])) end--;
        if (end == 0 || !ClosingDialogueQuotes.Contains(text[end - 1])) return text.Length;
        while (end > 0 && ClosingDialogueQuotes.Contains(text[end - 1])) end--;
        return end;
    }

    internal static IReadOnlyList<SentenceTokenReference> GetContentTokens(IReadOnlyList<SentenceTokenReference> tokens)
    {
        var start = 0;
        var end = tokens.Count;
        while (start < end && tokens[start].Token.Surface.All(OpeningDialogueQuotes.Contains)) start++;
        var text = string.Concat(tokens.Select(token => token.Token.Surface));
        var contentEnd = GetContentEnd(text);
        var remainingLength = text.Length;
        while (end > start && remainingLength - tokens[end - 1].Token.Surface.Length >= contentEnd)
        {
            remainingLength -= tokens[end - 1].Token.Surface.Length;
            end--;
        }
        return tokens.Skip(start).Take(end - start).ToArray();
    }

    internal static bool HaveMatchingContentTokens(IReadOnlyList<SentenceTokenReference> original, IReadOnlyList<SentenceTokenReference> current)
    {
        var left = GetContentTokens(original);
        var right = GetContentTokens(current);
        if (left.Count != right.Count) return false;
        if (left.Count == 0) return original.Select(token => token.Token).SequenceEqual(current.Select(token => token.Token));
        // Only the quote-induced offset changes; lexical metadata and offsets inside the content must agree.
        return left.Select(token => TokenIdentity(token.Token, left[0].Token.StartOffset))
            .SequenceEqual(right.Select(token => TokenIdentity(token.Token, right[0].Token.StartOffset)));
    }

    private static (string Surface, string Lemma, string OrthBase, string Reading, string BaseReading, string Pronunciation,
        PartsOfSpeech PartsOfSpeech, string ConjugationType, string ConjugationForm, string AType, int Offset)
        TokenIdentity(Token token, int contentStart)
        => (token.Surface, token.Lemma, token.OrthBase, token.Reading, token.BaseReading, token.Pronunciation,
            token.PartsOfSpeech, token.ConjugationType, token.ConjugationForm, token.AType, token.StartOffset - contentStart);

}

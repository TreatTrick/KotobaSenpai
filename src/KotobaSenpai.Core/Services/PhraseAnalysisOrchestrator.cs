using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Orchestrates phrase analysis: first performs local sentence segmentation and token references, then calls
/// the provider concurrently (one independent request per sentence, concurrency-limited), validates groups,
/// assigns session ids, maps geometry, and preserves provider order. Any failure only produces a retryable
/// warning and never throws through the recognition flow; local words/spans remain usable.
/// </summary>
public sealed class PhraseAnalysisOrchestrator
{
    // ponytail: concurrency cap of 4 — a typical VN dialogue box has 2~5 sentences, capping beyond that is gentle on the LLM provider; tune if 429s occur or higher throughput is needed.
    private const int MaxConcurrency = 4;

    private readonly ILlmPhraseAnalyzer _analyzer;
    private readonly SentenceSegmenter _segmenter;
    private readonly SentenceTokenBuilder _tokenBuilder;

    public PhraseAnalysisOrchestrator(
        ILlmPhraseAnalyzer analyzer,
        SentenceSegmenter? segmenter = null,
        SentenceTokenBuilder? tokenBuilder = null)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _segmenter = segmenter ?? new SentenceSegmenter();
        _tokenBuilder = tokenBuilder ?? throw new ArgumentNullException(nameof(tokenBuilder));
    }

    public async Task<PhraseAnalysisRun> AnalyzeAsync(
        Guid recognitionId,
        IReadOnlyList<OcrLine> lines,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId == Guid.Empty)
            throw new ArgumentException("Recognition id must not be empty.", nameof(recognitionId));
        ArgumentNullException.ThrowIfNull(lines);

        // First build the requests purely locally (fast, zero I/O), preserving segment order; Task.WhenAll returns results in input order, so no re-sorting is needed.
        var requests = BuildRequests(recognitionId, lines);
        if (requests.Count == 0)
            return new PhraseAnalysisRun(PhraseAnalysisOutcome.Success, [], null);

        PhraseAnalysisResult[] results;
        try
        {
            results = await RunThrottledAsync(requests, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(PhraseAnalysisOutcome.Cancelled, "Phrase analysis cancelled.", []);
        }

        // Evaluate each sentence independently: failed sentences are recorded as warnings and skipped, the rest proceed as usual, matching the "partially usable" positioning.
        var groups = new List<PhraseGroup>();
        var wordMeanings = new List<WordMeaningView>();
        var warnings = new List<string>();
        var successfulSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        PhraseAnalysisOutcome? firstFailure = null;

        for (int i = 0; i < results.Length; i++)
        {
            var result = results[i];
            if (!result.Succeeded)
            {
                var warning = result.Warning ?? DescribeFailure(result.Outcome);
                if (!warnings.Contains(warning))
                    warnings.Add(warning);
                firstFailure ??= result.Outcome;
                continue;
            }

            successfulSegmentIds.Add(requests[i].SegmentId);

            var tokenById = requests[i].Tokens.ToDictionary(reference => reference.Id);
            var validation = PhraseGroupValidator.ValidateAndBuild(result.Groups, tokenById);
            warnings.AddRange(validation.Warnings);
            groups.AddRange(validation.ValidGroups.Select(group => group.WithSessionId(Guid.NewGuid())));

            var wordValidation = WordMeaningValidator.ValidateAndBuild(result.Words, requests[i].LocalSpans);
            warnings.AddRange(wordValidation.Warnings);
            wordMeanings.AddRange(wordValidation.ValidWords);
        }

        // As long as any (processable) sentence succeeds, the overall outcome is Success; only when all fail is the first failure category taken.
        var outcome = groups.Count > 0 || results.Any(result => result.Succeeded)
            ? PhraseAnalysisOutcome.Success
            : firstFailure ?? PhraseAnalysisOutcome.Success;

        return new PhraseAnalysisRun(
            outcome,
            groups.Select(PhraseGeometryMapper.MapGroup).ToArray(),
            warnings.Count > 0 ? string.Join("; ", warnings) : null)
        {
            Words = wordMeanings.ToArray(),
            SuccessfulSegmentIds = successfulSegmentIds,
        };
    }

    private IReadOnlyList<PhraseAnalysisRequest> BuildRequests(Guid recognitionId, IReadOnlyList<OcrLine> lines)
    {
        var requests = new List<PhraseAnalysisRequest>();
        foreach (var segment in _segmenter.Segment(lines))
        {
            var segmentTokens = _tokenBuilder.Build(lines, segment);
            if (segmentTokens.References.Count == 0)
                continue;

            requests.Add(new PhraseAnalysisRequest(
                recognitionId,
                segment.SegmentId,
                string.Concat(segment.LineIndices.Select(i => lines[i].Text)),
                segmentTokens.References,
                segmentTokens.LocalSpans));
        }
        return requests;
    }

    // ponytail: throttle logic folded into a private method so the main flow stays flat and readable.
    private async Task<PhraseAnalysisResult[]> RunThrottledAsync(
        IReadOnlyList<PhraseAnalysisRequest> requests,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(MaxConcurrency);
        var tasks = requests.Select(async request =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _analyzer.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static PhraseAnalysisRun Failed(
        PhraseAnalysisOutcome outcome,
        string warning,
        IReadOnlyList<string> priorWarnings)
    {
        var all = priorWarnings.ToList();
        all.Add(warning);
        return new PhraseAnalysisRun(outcome, Array.Empty<PhraseGroupView>(), string.Join("; ", all));
    }

    private static string DescribeFailure(PhraseAnalysisOutcome outcome) => outcome switch
    {
        PhraseAnalysisOutcome.NoKey => "Phrase analysis requires provider configuration.",
        PhraseAnalysisOutcome.Timeout => "Phrase analysis provider timed out.",
        PhraseAnalysisOutcome.Refused => "Phrase analysis provider refused the request.",
        PhraseAnalysisOutcome.MalformedJson => "Phrase analysis provider returned malformed JSON.",
        PhraseAnalysisOutcome.TransportError => "Phrase analysis provider transport error.",
        PhraseAnalysisOutcome.InvalidResponse => "Phrase analysis provider returned an invalid response.",
        _ => "Phrase analysis failed.",
    };
}

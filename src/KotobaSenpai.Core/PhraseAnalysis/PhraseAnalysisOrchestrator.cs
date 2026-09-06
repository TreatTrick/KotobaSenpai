using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>
/// Orchestrates phrase analysis: first performs local sentence segmentation and token references, then calls
/// the provider concurrently (one independent request per sentence, concurrency-limited), validates groups,
/// assigns session ids, maps geometry, and preserves provider order. Any failure only produces a retryable
/// warning and never throws through the recognition flow; local words/spans remain usable.
/// Supports sentence-level LLM result caching: unchanged sentences reuse cached results across incremental recognition cycles.
/// </summary>
public sealed class PhraseAnalysisOrchestrator
{
    // ponytail: concurrency cap of 4 — a typical VN dialogue box has 2~5 sentences, capping beyond that is gentle on the LLM provider; tune if 429s occur or higher throughput is needed.
    private const int MaxConcurrency = 4;
    // ponytail: cache cap of 50 — enough for typical VN sessions; FIFO eviction keeps memory bounded.
    private const int MaxCacheSize = 50;

    private readonly ILlmPhraseAnalyzer _analyzer;
    private readonly SentenceSegmenter _segmenter;
    private readonly SentenceTokenBuilder _tokenBuilder;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, PhraseAnalysisResult> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _cacheOrder = new();
    private readonly Dictionary<(string SegmentText, string ModelGroupId), Guid> _sessionGroupIds = new();

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
            var (cached, uncached) = SplitByCache(requests);
            var freshResults = uncached.Count > 0
                ? await RunThrottledAsync(uncached, cancellationToken).ConfigureAwait(false)
                : Array.Empty<PhraseAnalysisResult>();
            // Write successful fresh results to cache
            for (int i = 0; i < uncached.Count; i++)
            {
                if (freshResults[i].Succeeded)
                    CacheResult(uncached[i].SegmentText, freshResults[i]);
            }
            // Merge: build a lookup for fresh results by request index in the uncached list
            var freshByIndex = new Dictionary<int, PhraseAnalysisResult>();
            for (int i = 0; i < uncached.Count; i++)
                freshByIndex[uncached[i].OriginalIndex] = freshResults[i];
            // Assemble final results array in original request order
            results = new PhraseAnalysisResult[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                if (freshByIndex.TryGetValue(i, out var fresh))
                    results[i] = fresh;
                else
                    results[i] = cached[i];
            }
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
            groups.AddRange(validation.ValidGroups.Select(group => group.WithSessionId(
                GetSessionGroupId(requests[i].SegmentText, group.ModelGroupId))));

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
        IReadOnlyList<CacheableRequest> requests,
        CancellationToken cancellationToken)
    {
        using var throttler = new SemaphoreSlim(MaxConcurrency);
        var tasks = requests.Select(async cacheableRequest =>
        {
            await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _analyzer.AnalyzeAsync(cacheableRequest.Request, cancellationToken).ConfigureAwait(false);
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

    private (Dictionary<int, PhraseAnalysisResult> cached, List<CacheableRequest> uncached) SplitByCache(
        IReadOnlyList<PhraseAnalysisRequest> requests)
    {
        var cached = new Dictionary<int, PhraseAnalysisResult>();
        var uncached = new List<CacheableRequest>();
        for (int i = 0; i < requests.Count; i++)
        {
            if (TryGetCached(requests[i].SegmentText, out var result))
                cached[i] = result;
            else
                uncached.Add(new CacheableRequest(requests[i], i));
        }
        return (cached, uncached);
    }

    private void CacheResult(string segmentText, PhraseAnalysisResult result)
    {
        lock (_cacheGate)
        {
            if (_cache.ContainsKey(segmentText))
            {
                _cache[segmentText] = result;
                return;
            }

            if (_cache.Count >= MaxCacheSize)
            {
                var oldest = _cacheOrder.First!;
                _cache.Remove(oldest.Value);
                _cacheOrder.RemoveFirst();
                foreach (var key in _sessionGroupIds.Keys
                    .Where(key => string.Equals(key.SegmentText, oldest.Value, StringComparison.Ordinal))
                    .ToArray())
                    _sessionGroupIds.Remove(key);
            }
            _cache[segmentText] = result;
            _cacheOrder.AddLast(segmentText);
        }
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _cacheOrder.Clear();
            _sessionGroupIds.Clear();
        }
    }

    private bool TryGetCached(string segmentText, out PhraseAnalysisResult result)
    {
        lock (_cacheGate)
            return _cache.TryGetValue(segmentText, out result!);
    }

    private Guid GetSessionGroupId(string segmentText, string modelGroupId)
    {
        lock (_cacheGate)
        {
            var key = (segmentText, modelGroupId);
            if (_sessionGroupIds.TryGetValue(key, out var id))
                return id;
            id = Guid.NewGuid();
            _sessionGroupIds[key] = id;
            return id;
        }
    }

    private sealed record CacheableRequest(PhraseAnalysisRequest Request, int OriginalIndex)
    {
        public string SegmentText => Request.SegmentText;
    };
}

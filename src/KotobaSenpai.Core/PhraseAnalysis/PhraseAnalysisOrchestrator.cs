using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Session;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>
/// Orchestrates phrase analysis: first prepares stable text blocks and token references, then calls
/// the provider concurrently (one independent request per sentence, concurrency-limited), validates groups,
/// assigns session ids, maps geometry, and preserves provider order. Any failure only produces a retryable
/// warning and never throws through the recognition flow; local words/spans remain usable.
/// Supports sentence-level LLM result caching: unchanged sentences reuse cached results across incremental recognition cycles.
/// </summary>
public sealed class PhraseAnalysisOrchestrator
{
    private readonly SentenceBlockLayout _layout;
    private readonly PhraseAnalysisCache _cache;
    private readonly PhraseRunAssembler _assembler;
    private readonly IExplanationLanguagePreferences? _explanationLanguage;
    private readonly ILogger? _logger;
    private readonly SentenceTranslationPreferences? _sentenceTranslation;
    private readonly SessionEpoch _sessionEpoch;

    public PhraseAnalysisOrchestrator(
        ILlmPhraseAnalyzer analyzer,
        SentenceSegmenter? segmenter = null,
        SentenceTokenBuilder? tokenBuilder = null,
        IExplanationLanguagePreferences? explanationLanguage = null,
        ILogger? logger = null,
        SentenceTranslationPreferences? sentenceTranslation = null,
        SessionEpoch? sessionEpoch = null)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        _layout = new SentenceBlockLayout(segmenter ?? new SentenceSegmenter(),
            tokenBuilder ?? throw new ArgumentNullException(nameof(tokenBuilder)));
        _sessionEpoch = sessionEpoch ?? new SessionEpoch();
        _assembler = new PhraseRunAssembler(_sessionEpoch);
        _cache = new PhraseAnalysisCache(analyzer, _assembler, _sessionEpoch);
        _explanationLanguage = explanationLanguage;
        _logger = logger;
        _sentenceTranslation = sentenceTranslation;
    }

    public SessionEpoch SessionEpoch => _sessionEpoch;

    /// <summary>Rebuilds successful cached annotations against the current OCR geometry without calling the provider.</summary>
    public PhraseAnalysisRun GetCachedAnalysis(Guid recognitionId, IReadOnlyList<OcrLine> lines)
        => GetCachedAnalysis(PrepareAnalysis(recognitionId, lines));

    public PhraseAnalysisRun GetCachedAnalysis(PhraseAnalysisPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_cache.IsCurrent(plan.Epoch))
            return new PhraseAnalysisRun(PhraseAnalysisOutcome.Success, [], null);
        var requests = plan.Requests;
        var (cached, _) = _cache.SplitByCache(requests);
        var indices = cached.Keys.Order().ToArray();
        return _assembler.BuildRun(indices.Select(index => requests[index]).ToArray(),
            indices.Select(index => cached[index]).ToArray(), plan.Epoch);
    }

    public Task<PhraseAnalysisRun> AnalyzeAsync(
        Guid recognitionId,
        IReadOnlyList<OcrLine> lines,
        CancellationToken cancellationToken = default)
        => AnalyzeAsync(PrepareAnalysis(recognitionId, lines), cancellationToken);

    public async Task<PhraseAnalysisRun> AnalyzeAsync(PhraseAnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_cache.IsCurrent(plan.Epoch))
            return Cancelled("The prepared analysis was superseded by a cache reset.");
        var recognitionId = plan.RecognitionId;
        var requests = plan.Requests;
        if (requests.Count == 0)
            return new PhraseAnalysisRun(PhraseAnalysisOutcome.Success, [], null);

        PhraseAnalysisResult[] results;
        try
        {
            var (cached, uncached) = _cache.SplitByCache(requests);
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                _logger?.Log(LogLevel.Debug, null,
                    "[PHRASE] Recognition {0:N}: segment={1}, cached={2}, tokens={3}, source='{4}', normalized='{5}'.",
                    recognitionId, request.SegmentId, cached.ContainsKey(i), request.Tokens.Count,
                    request.SegmentText, SentenceBlockLayout.NormalizeSegmentText(request.SegmentText));
            }
            var tasks = new List<Task<PhraseAnalysisResult>>();
            var sharedRequests = 0;
            foreach (var item in uncached)
            {
                tasks.Add(_cache.GetOrStartAnalysis(item.Request, plan.Epoch, cancellationToken, out var shared));
                if (shared) sharedRequests++;
            }
            _logger?.LogInformation("[PHRASE] Recognition {0:N}: segments={1}, cacheHits={2}, providerRequests={3}.",
                recognitionId, requests.Count, cached.Count, uncached.Count - sharedRequests);
            if (sharedRequests > 0)
                _logger?.LogInformation("[PHRASE] Recognition {0:N}: inFlightHits={1}.", recognitionId, sharedRequests);
            var freshResults = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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
            return Cancelled("Phrase analysis cancelled.");
        }

        _cache.RememberCompletedBlocks(plan.Epoch, requests, results);
        return _assembler.BuildRun(requests, results, plan.Epoch);
    }

    /// <summary>Retains completed block boundaries before either local grouping or provider analysis begins.</summary>
    public PhraseAnalysisPlan PrepareAnalysis(Guid recognitionId, IReadOnlyList<OcrLine> lines)
    {
        if (recognitionId == Guid.Empty)
            throw new ArgumentException("Recognition id must not be empty.", nameof(recognitionId));
        ArgumentNullException.ThrowIfNull(lines);

        var language = _explanationLanguage?.EffectiveLanguage ?? "English";
        var translation = _sentenceTranslation?.Enabled ?? true;
        var (epoch, boundaries) = _cache.CaptureBoundaries();
        return _layout.Prepare(recognitionId, epoch, lines, boundaries, language, translation);
    }

    public void ClearCache() => _cache.ClearCache();

    internal SessionEpochSnapshot ClearCacheAndCaptureEpoch() => _cache.ClearCache();

    private static PhraseAnalysisRun Cancelled(string warning)
        => new(PhraseAnalysisOutcome.Cancelled, [], warning);
}

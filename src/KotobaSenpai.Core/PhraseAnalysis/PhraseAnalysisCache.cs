using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Session;
using static KotobaSenpai.Core.PhraseAnalysis.SentenceBlockLayout;
using static KotobaSenpai.Core.PhraseAnalysis.PhraseRunAssembler;

namespace KotobaSenpai.Core.PhraseAnalysis;

/// <summary>Owns bounded sentence caching, reset epochs, shared requests and provider concurrency.</summary>
internal sealed class PhraseAnalysisCache(ILlmPhraseAnalyzer analyzer, PhraseRunAssembler assembler, SessionEpoch sessionEpoch)
{
    // ponytail: four provider slots cover typical dialogue without encouraging rate-limit failures.
    private const int MaxConcurrency = 4;
    // ponytail: fifty sentences with FIFO eviction bound session memory without a cache dependency.
    private const int MaxCacheSize = 50;
    private readonly ILlmPhraseAnalyzer _analyzer = analyzer;
    private readonly PhraseRunAssembler _assembler = assembler;
    private readonly SessionEpoch _sessionEpoch = sessionEpoch;
    private readonly object _cacheGate = new();
    private readonly SemaphoreSlim _providerSlots = new(MaxConcurrency);
    private readonly Dictionary<(string SegmentText, string Language, bool Translation), PendingAnalysis> _inFlight = new();
    private IReadOnlyList<PhraseAnalysisRequest> _completedBlocks = [];
    private readonly Dictionary<(string SegmentText, string Language, bool Translation), CachedAnalysis> _cache = new();
    private readonly LinkedList<(string SegmentText, string Language, bool Translation)> _cacheOrder = new();

    public bool IsCurrent(long epoch) => _sessionEpoch.IsCurrent(epoch);

    public (long Epoch, IReadOnlyList<PhraseAnalysisRequest> Boundaries) CaptureBoundaries()
    {
        lock (_cacheGate)
            return (_sessionEpoch.Capture().Value, _completedBlocks.Concat(_inFlight.Values
                    .Where(pending => !pending.CancellationToken.IsCancellationRequested
                        && _sessionEpoch.IsCurrent(pending.Epoch))
                    .Select(pending => pending.Request))
                .Where(request => HasCompletedBoundary(request.SegmentText)).ToArray());
    }

    public void RememberCompletedBlocks(long epoch, IReadOnlyList<PhraseAnalysisRequest> requests,
        IReadOnlyList<PhraseAnalysisResult> results)
    {
        lock (_cacheGate)
            if (_sessionEpoch.IsCurrent(epoch))
                _completedBlocks = requests.Where((request, index) => results[index].Succeeded
                    && HasCompletedBoundary(request.SegmentText)).ToArray();
    }

    public Task<PhraseAnalysisResult> GetOrStartAnalysis(PhraseAnalysisRequest request, long epoch,
        CancellationToken cancellationToken, out bool shared)
    {
        PendingAnalysis pending;
        var key = GetCacheKey(request);
        lock (_cacheGate)
        {
            if (!_sessionEpoch.IsCurrent(epoch))
                throw new OperationCanceledException("The prepared analysis was superseded by a cache reset.");
            // Another batch may have finished between the initial cache lookup and scheduling.
            if (TryGetCached(request, out var cached))
            {
                shared = true;
                return Task.FromResult(cached);
            }
            if (_inFlight.TryGetValue(key, out pending!)
                && !pending.CancellationToken.IsCancellationRequested
                && _sessionEpoch.IsCurrent(pending.Epoch)
                && HaveMatchingContentTokens(pending.Request.Tokens, request.Tokens))
            {
                shared = true;
                return AwaitAnalysisAsync(pending, request, cancellationToken);
            }
            shared = false;
            pending = new PendingAnalysis(request, epoch, cancellationToken,
                new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously));
            _inFlight[key] = pending;
        }
        _ = RunProviderAsync(request, pending, cancellationToken);
        // The owner tracks the actual transport lifetime; shared waiters can cancel their own wait independently.
        return AwaitAnalysisAsync(pending, request, CancellationToken.None);
    }

    private async Task<PhraseAnalysisResult> AwaitAnalysisAsync(PendingAnalysis pending, PhraseAnalysisRequest request, CancellationToken cancellationToken)
    {
        var result = await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return RebindResult(request, pending.Request.Tokens, result);
    }

    private async Task RunProviderAsync(PhraseAnalysisRequest request, PendingAnalysis pending, CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            await _providerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _analyzer.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Succeeded) CacheResult(request, result, pending.Epoch);
            pending.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException exception) { pending.Completion.TrySetCanceled(exception.CancellationToken); }
        catch (Exception exception) { pending.Completion.TrySetException(exception); }
        finally
        {
            if (entered) _providerSlots.Release();
            var key = GetCacheKey(request);
            lock (_cacheGate)
                if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, pending)) _inFlight.Remove(key);
        }
    }

    public (Dictionary<int, PhraseAnalysisResult> cached, List<CacheableRequest> uncached) SplitByCache(
        IReadOnlyList<PhraseAnalysisRequest> requests)
    {
        var cached = new Dictionary<int, PhraseAnalysisResult>();
        var uncached = new List<CacheableRequest>();
        for (int i = 0; i < requests.Count; i++)
        {
            if (TryGetCached(requests[i], out var result))
                cached[i] = result;
            else
                uncached.Add(new CacheableRequest(requests[i], i));
        }
        return (cached, uncached);
    }

    private void CacheResult(PhraseAnalysisRequest request, PhraseAnalysisResult result, long epoch)
    {
        var key = GetCacheKey(request);
        var cached = new CachedAnalysis(request.Tokens, result);
        lock (_cacheGate)
        {
            if (!_sessionEpoch.IsCurrent(epoch)) return;
            if (_cache.ContainsKey(key))
            {
                _cache[key] = cached;
                return;
            }

            if (_cache.Count >= MaxCacheSize)
            {
                var oldest = _cacheOrder.First!;
                _cache.Remove(oldest.Value);
                _cacheOrder.RemoveFirst();
                _assembler.ForgetSentence(oldest.Value);
            }
            _cache[key] = cached;
            _cacheOrder.AddLast(key);
        }
    }

    public SessionEpochSnapshot ClearCache()
    {
        lock (_cacheGate)
        {
            var epoch = _sessionEpoch.Advance();
            _inFlight.Clear();
            _completedBlocks = [];
            _cache.Clear();
            _cacheOrder.Clear();
            _assembler.ClearSessionIds();
            return epoch;
        }
    }

    private bool TryGetCached(PhraseAnalysisRequest request, out PhraseAnalysisResult result)
    {
        CachedAnalysis? cached;
        lock (_cacheGate)
            _cache.TryGetValue(GetCacheKey(request), out cached);

        result = null!;
        if (cached is null || !HaveMatchingContentTokens(cached.Tokens, request.Tokens))
            return false;

        result = RebindResult(request, cached.Tokens, cached.Result);
        return true;
    }

    private static (string SegmentText, string Language, bool Translation) GetCacheKey(PhraseAnalysisRequest request)
        => (NormalizeSegmentText(request.SegmentText), request.ExplanationLanguage, request.IncludeSentenceTranslation);

    internal sealed record CacheableRequest(PhraseAnalysisRequest Request, int OriginalIndex);

    private sealed record CachedAnalysis(IReadOnlyList<SentenceTokenReference> Tokens, PhraseAnalysisResult Result);
    private sealed record PendingAnalysis(PhraseAnalysisRequest Request, long Epoch, CancellationToken CancellationToken,
        TaskCompletionSource<PhraseAnalysisResult> Completion);
}

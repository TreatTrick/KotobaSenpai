using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

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
    // ponytail: concurrency cap of 4 — a typical VN dialogue box has 2~5 sentences, capping beyond that is gentle on the LLM provider; tune if 429s occur or higher throughput is needed.
    private const int MaxConcurrency = 4;
    // ponytail: cache cap of 50 — enough for typical VN sessions; FIFO eviction keeps memory bounded.
    private const int MaxCacheSize = 50;
    private static readonly char[] OpeningDialogueQuotes = ['「', '『'];
    private static readonly char[] ClosingDialogueQuotes = ['」', '』'];

    private readonly ILlmPhraseAnalyzer _analyzer;
    private readonly SentenceSegmenter _segmenter;
    private readonly SentenceTokenBuilder _tokenBuilder;
    private readonly IExplanationLanguagePreferences? _explanationLanguage;
    private readonly ILogger? _logger;
    private readonly SentenceTranslationPreferences? _sentenceTranslation;
    private readonly object _cacheGate = new();
    private readonly SemaphoreSlim _providerSlots = new(MaxConcurrency);
    private readonly Dictionary<(string SegmentText, string Language, bool Translation), PendingAnalysis> _inFlight = new();
    private int _cacheGeneration;
    private IReadOnlyList<PhraseAnalysisRequest> _completedBlocks = [];
    private readonly Dictionary<(string SegmentText, string Language, bool Translation), CachedAnalysis> _cache = new();
    private readonly LinkedList<(string SegmentText, string Language, bool Translation)> _cacheOrder = new();
    private readonly Dictionary<(string SegmentText, string Language, bool Translation, string ModelGroupId), Guid> _sessionGroupIds = new();

    public PhraseAnalysisOrchestrator(
        ILlmPhraseAnalyzer analyzer,
        SentenceSegmenter? segmenter = null,
        SentenceTokenBuilder? tokenBuilder = null,
        IExplanationLanguagePreferences? explanationLanguage = null,
        ILogger? logger = null,
        SentenceTranslationPreferences? sentenceTranslation = null)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _segmenter = segmenter ?? new SentenceSegmenter();
        _tokenBuilder = tokenBuilder ?? throw new ArgumentNullException(nameof(tokenBuilder));
        _explanationLanguage = explanationLanguage;
        _logger = logger;
        _sentenceTranslation = sentenceTranslation;
    }

    /// <summary>Rebuilds successful cached annotations against the current OCR geometry without calling the provider.</summary>
    public PhraseAnalysisRun GetCachedAnalysis(Guid recognitionId, IReadOnlyList<OcrLine> lines)
        => GetCachedAnalysis(PrepareAnalysis(recognitionId, lines));

    public PhraseAnalysisRun GetCachedAnalysis(PhraseAnalysisPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CacheGeneration != Volatile.Read(ref _cacheGeneration))
            return new PhraseAnalysisRun(PhraseAnalysisOutcome.Success, [], null);
        var requests = plan.Requests;
        var (cached, _) = SplitByCache(requests);
        var indices = cached.Keys.Order().ToArray();
        return BuildRun(indices.Select(index => requests[index]).ToArray(),
            indices.Select(index => cached[index]).ToArray());
    }

    public Task<PhraseAnalysisRun> AnalyzeAsync(
        Guid recognitionId,
        IReadOnlyList<OcrLine> lines,
        CancellationToken cancellationToken = default)
        => AnalyzeAsync(PrepareAnalysis(recognitionId, lines), cancellationToken);

    public async Task<PhraseAnalysisRun> AnalyzeAsync(PhraseAnalysisPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CacheGeneration != Volatile.Read(ref _cacheGeneration))
            return Failed(PhraseAnalysisOutcome.Cancelled, "The prepared analysis was superseded by a cache reset.", []);
        var recognitionId = plan.RecognitionId;
        var requests = plan.Requests;
        if (requests.Count == 0)
            return new PhraseAnalysisRun(PhraseAnalysisOutcome.Success, [], null);

        PhraseAnalysisResult[] results;
        try
        {
            var (cached, uncached) = SplitByCache(requests);
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                _logger?.Log(LogLevel.Debug, null,
                    "[PHRASE] Recognition {0:N}: segment={1}, cached={2}, tokens={3}, source='{4}', normalized='{5}'.",
                    recognitionId, request.SegmentId, cached.ContainsKey(i), request.Tokens.Count,
                    request.SegmentText, NormalizeSegmentText(request.SegmentText));
            }
            var tasks = new List<Task<PhraseAnalysisResult>>();
            var sharedRequests = 0;
            foreach (var item in uncached)
            {
                tasks.Add(GetOrStartAnalysis(item.Request, plan.CacheGeneration, cancellationToken, out var shared));
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
            return Failed(PhraseAnalysisOutcome.Cancelled, "Phrase analysis cancelled.", []);
        }

        lock (_cacheGate)
            if (plan.CacheGeneration == _cacheGeneration)
                _completedBlocks = requests.Where((request, index) => results[index].Succeeded
                    && HasCompletedBoundary(request.SegmentText)).ToArray();
        return BuildRun(requests, results);
    }

    private PhraseAnalysisRun BuildRun(IReadOnlyList<PhraseAnalysisRequest> requests, IReadOnlyList<PhraseAnalysisResult> results)
    {
        // Evaluate each sentence independently: failed sentences are recorded as warnings and skipped, the rest proceed as usual, matching the "partially usable" positioning.
        var groups = new List<PhraseGroupView>();
        var wordMeanings = new List<WordMeaningView>();
        var sentences = new List<SentenceTranslation>();
        var warnings = new List<string>();
        var successfulSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        PhraseAnalysisOutcome? firstFailure = null;

        for (int i = 0; i < results.Count; i++)
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
            var request = requests[i];
            sentences.Add(new SentenceTranslation(request.SegmentId, request.SegmentText, request.ExplanationLanguage,
                request.IncludeSentenceTranslation, request.IncludeSentenceTranslation ? SentenceTranslation.Validate(result.Translation) : null));

            var tokenById = requests[i].Tokens.ToDictionary(reference => reference.Id);
            var validation = PhraseGroupValidator.ValidateAndBuild(result.Groups, tokenById);
            warnings.AddRange(validation.Warnings);
            groups.AddRange(validation.ValidGroups.Select(group => PhraseGeometryMapper.MapGroup(group.WithSessionId(
                GetSessionGroupId(request, group.ModelGroupId))) with { SegmentId = request.SegmentId }));

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
            groups.ToArray(),
            warnings.Count > 0 ? string.Join("; ", warnings) : null)
        {
            Words = wordMeanings.ToArray(),
            SuccessfulSegmentIds = successfulSegmentIds,
            Sentences = sentences.ToArray(),
        };
    }

    /// <summary>Retains completed block boundaries before either local grouping or provider analysis begins.</summary>
    public PhraseAnalysisPlan PrepareAnalysis(Guid recognitionId, IReadOnlyList<OcrLine> lines)
    {
        if (recognitionId == Guid.Empty)
            throw new ArgumentException("Recognition id must not be empty.", nameof(recognitionId));
        ArgumentNullException.ThrowIfNull(lines);

        var language = _explanationLanguage?.EffectiveLanguage ?? "English";
        var translation = _sentenceTranslation?.Enabled ?? true;
        int generation;
        IReadOnlyList<PhraseAnalysisRequest> boundaries;
        lock (_cacheGate)
        {
            generation = _cacheGeneration;
            boundaries = _completedBlocks.Concat(_inFlight.Values
                    .Where(pending => !pending.CancellationToken.IsCancellationRequested)
                    .Select(pending => pending.Request))
                .Where(request => HasCompletedBoundary(request.SegmentText))
                .ToArray();
        }
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

    private static bool HasCompletedBoundary(string text)
        => text.Length > 0 && (GetContentEnd(text) < text.Length || SentenceSegmenter.IsSentenceFinal(text));

    private Task<PhraseAnalysisResult> GetOrStartAnalysis(PhraseAnalysisRequest request, int generation,
        CancellationToken cancellationToken, out bool shared)
    {
        PendingAnalysis pending;
        var key = GetCacheKey(request);
        lock (_cacheGate)
        {
            if (generation != _cacheGeneration)
                throw new OperationCanceledException("The prepared analysis was superseded by a cache reset.");
            // Another batch may have finished between the initial cache lookup and scheduling.
            if (TryGetCached(request, out var cached))
            {
                shared = true;
                return Task.FromResult(cached);
            }
            if (_inFlight.TryGetValue(key, out pending!)
                && !pending.CancellationToken.IsCancellationRequested
                && HaveMatchingContentTokens(pending.Request.Tokens, request.Tokens))
            {
                shared = true;
                return AwaitAnalysisAsync(pending, request, cancellationToken);
            }
            shared = false;
            pending = new PendingAnalysis(request, generation, cancellationToken,
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
            if (result.Succeeded) CacheResult(request, result, pending.Generation);
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
            if (TryGetCached(requests[i], out var result))
                cached[i] = result;
            else
                uncached.Add(new CacheableRequest(requests[i], i));
        }
        return (cached, uncached);
    }

    private void CacheResult(PhraseAnalysisRequest request, PhraseAnalysisResult result, int generation)
    {
        var key = GetCacheKey(request);
        var cached = new CachedAnalysis(request.Tokens, result);
        lock (_cacheGate)
        {
            if (generation != _cacheGeneration) return;
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
                foreach (var groupKey in _sessionGroupIds.Keys
                    .Where(groupKey => (groupKey.SegmentText, groupKey.Language, groupKey.Translation) == oldest.Value)
                    .ToArray())
                    _sessionGroupIds.Remove(groupKey);
            }
            _cache[key] = cached;
            _cacheOrder.AddLast(key);
        }
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _inFlight.Clear();
            _completedBlocks = [];
            _cache.Clear();
            _cacheOrder.Clear();
            _sessionGroupIds.Clear();
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

    private static PhraseAnalysisResult RebindResult(PhraseAnalysisRequest request,
        IReadOnlyList<SentenceTokenReference> tokens, PhraseAnalysisResult result)
    {
        if (tokens.Select(token => (token.Id, token.Token))
            .SequenceEqual(request.Tokens.Select(token => (token.Id, token.Token))))
            return result;

        // Quote recognition can shift every token id; bind unchanged content to the current OCR references.
        var content = GetContentTokens(tokens);
        var currentContent = GetContentTokens(request.Tokens);
        var ids = content.Select((token, index) => (token.Id, CurrentId: currentContent[index].Id))
            .ToDictionary(pair => pair.Id, pair => pair.CurrentId);
        var validGroupIds = PhraseGroupValidator.ValidateAndBuild(result.Groups, tokens.ToDictionary(token => token.Id))
            .ValidGroups.Select(group => group.ModelGroupId).ToHashSet(StringComparer.Ordinal);
        var groups = result.Groups
            // Removing a quote must not turn an originally invalid provider group into a valid group.
            .Where(group => validGroupIds.Contains(group.ModelGroupId))
            .DistinctBy(group => group.ModelGroupId, StringComparer.Ordinal)
            .Select(group => new ParsedPhraseGroup(group.ModelGroupId, group.Type,
                group.PartTokenIds.Select(part => (IReadOnlyList<SentenceTokenId>)part.Where(ids.ContainsKey).Select(id => ids[id]).ToArray())
                    .Where(part => part.Count > 0).ToArray(),
                group.Label, group.Meaning, group.Grammar))
            .Where(group => group.PartTokenIds.Count > 0)
            .ToArray();
        return new PhraseAnalysisResult(result.Outcome, groups, result.Warning)
        {
            Words = result.Words,
            Translation = result.Translation,
            Endpoint = result.Endpoint,
            ProviderStatusCode = result.ProviderStatusCode,
        };
    }

    private Guid GetSessionGroupId(PhraseAnalysisRequest request, string modelGroupId)
    {
        lock (_cacheGate)
        {
            var key = (NormalizeSegmentText(request.SegmentText), request.ExplanationLanguage, request.IncludeSentenceTranslation, modelGroupId);
            if (_sessionGroupIds.TryGetValue(key, out var id))
                return id;
            id = Guid.NewGuid();
            _sessionGroupIds[key] = id;
            return id;
        }
    }

    private static (string SegmentText, string Language, bool Translation) GetCacheKey(PhraseAnalysisRequest request)
        => (NormalizeSegmentText(request.SegmentText), request.ExplanationLanguage, request.IncludeSentenceTranslation);

    private static string NormalizeSegmentText(string text)
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

    private static IReadOnlyList<SentenceTokenReference> GetContentTokens(IReadOnlyList<SentenceTokenReference> tokens)
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

    private static bool HaveMatchingContentTokens(IReadOnlyList<SentenceTokenReference> original, IReadOnlyList<SentenceTokenReference> current)
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

    private sealed record CacheableRequest(PhraseAnalysisRequest Request, int OriginalIndex);

    private sealed record CachedAnalysis(IReadOnlyList<SentenceTokenReference> Tokens, PhraseAnalysisResult Result);
    private sealed record PendingAnalysis(PhraseAnalysisRequest Request, int Generation, CancellationToken CancellationToken,
        TaskCompletionSource<PhraseAnalysisResult> Completion);
}

using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Logging;
using System.Globalization;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Core.Tests.PhraseAnalysis;

public sealed class PhraseAnalysisCacheTests
{
    [Fact]
    public async Task Completed_block_boundaries_survive_translation_mode_changes_without_reusing_the_wrong_results()
    {
        var preferences = new SentenceTranslationPreferences(new TranslationSettings());
        var analyzer = new PendingAnalyzer();
        analyzer.Release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        var orchestrator = new PhraseAnalysisOrchestrator(analyzer, tokenBuilder: new SentenceTokenBuilder(new CharTokenizer()),
            sentenceTranslation: preferences);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」")]);
        preferences.Enabled = false;
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」という説明。")]);
        preferences.Enabled = true;
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」という説明。")]);

        Assert.Equal([("「引用」", true), ("「引用」", false), ("という説明。", false), ("という説明。", true)],
            analyzer.Requests.Select(request => (request.SegmentText, request.IncludeSentenceTranslation)));
    }

    [Fact]
    public async Task Completed_block_boundaries_are_forgotten_when_the_cache_is_cleared()
    {
        var analyzer = new PendingAnalyzer();
        analyzer.Release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        var orchestrator = Create(analyzer);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」")]);
        orchestrator.ClearCache();

        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」"), Line("という説明。", 30)]);

        Assert.Equal(["「引用」", "「引用」という説明。"], analyzer.Requests.Select(request => request.SegmentText));
    }

    [Theory]
    [InlineData("続き", PhraseAnalysisOutcome.Success)]
    [InlineData("「引用」", PhraseAnalysisOutcome.TransportError)]
    public async Task Incomplete_or_failed_blocks_do_not_freeze_a_boundary(string previous, PhraseAnalysisOutcome outcome)
    {
        var analyzer = new PendingAnalyzer();
        analyzer.Release.SetResult(new PhraseAnalysisResult(outcome, []));
        var orchestrator = Create(analyzer);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line(previous)]);

        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line(previous), Line("という説明。", 30)]);

        Assert.Equal([previous, previous + "という説明。"], analyzer.Requests.Select(request => request.SegmentText));
    }

    [Fact]
    public async Task A_completed_block_in_flight_is_not_resent_with_appended_narration()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var first = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」")]);
        var second = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「引用」という説明。")]);
        try
        {
            Assert.Equal(["「引用」", "という説明。"], analyzer.Requests.Select(request => request.SegmentText));
        }
        finally
        {
            analyzer.Release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task A_plan_prepared_before_cache_reset_does_not_repopulate_the_cache()
    {
        var analyzer = new PendingAnalyzer();
        analyzer.Release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        var orchestrator = Create(analyzer);
        var plan = orchestrator.PrepareAnalysis(Guid.NewGuid(), [Line("旧句。")]);
        orchestrator.ClearCache();

        await orchestrator.AnalyzeAsync(plan);

        Assert.Empty(analyzer.Requests);
        Assert.Empty(orchestrator.GetCachedAnalysis(Guid.NewGuid(), [Line("旧句。")]).SuccessfulSegmentIds);
    }

    [Fact]
    public async Task Groups_covering_boundary_quotes_keep_only_current_content_references_after_rebinding()
    {
        var analyzer = new ContentAnalyzer(includeQuotes: true);
        var orchestrator = Create(analyzer);
        var previous = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「同じ。」")]);
        var current = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);

        Assert.Equal(1, analyzer.CallCount);
        var group = Assert.Single(current.Groups);
        Assert.Equal(Assert.Single(previous.Groups).SessionGroupId, group.SessionGroupId);
        Assert.Equal("同じ。", Assert.Single(group.Parts).Surface);
        Assert.Equal(new ScreenRect(0, 0, 30, 20), Assert.Single(Assert.Single(group.Parts).Rects));
        Assert.Null(current.Warning);
    }

    [Fact]
    public async Task An_invalid_group_cannot_become_valid_by_removing_repeated_boundary_quote_references()
    {
        var analyzer = new ContentAnalyzer(includeQuotes: true, repeatFirstToken: true);
        var orchestrator = Create(analyzer);
        var previous = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「同じ。」")]);
        Assert.Empty(previous.Groups);

        var current = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);

        Assert.Equal(1, analyzer.CallCount);
        Assert.Empty(current.Groups);
    }

    [Theory]
    [InlineData("同じ。", "「同じ。", 10)]
    [InlineData("「同じ。", "同じ。", 0)]
    [InlineData("『同じ。』", "同じ。", 0)]
    [InlineData("同じ」", "同じ」、", 0)]
    [InlineData("同じ」、", "同じ」", 0)]
    [InlineData("「同じ。」", "同じ。」。", 0)]
    public async Task Boundary_quote_OCR_changes_reuse_analysis_and_rebind_current_token_offsets(string firstText, string nextText, int x)
    {
        var analyzer = new ContentAnalyzer();
        var orchestrator = Create(analyzer);
        var first = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line(firstText)]);
        var preview = orchestrator.GetCachedAnalysis(Guid.NewGuid(), [Line(nextText)]);

        Assert.Contains("s0-0", preview.SuccessfulSegmentIds);
        var next = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line(nextText)]);

        Assert.Equal(1, analyzer.CallCount);
        var group = Assert.Single(next.Groups);
        Assert.Equal(Assert.Single(first.Groups).SessionGroupId, group.SessionGroupId);
        var part = Assert.Single(group.Parts);
        Assert.Equal("同じ", part.Surface);
        Assert.Equal(new ScreenRect(x, 0, 20, 20), Assert.Single(part.Rects));
        Assert.Equal(nextText, Assert.Single(next.Sentences).SourceText);
        Assert.Null(next.Warning);
    }

    [Theory]
    [InlineData("来る。", "来ない。")]
    [InlineData("彼は「行く」と言う。", "彼は行くと言う。")]
    [InlineData("同じ。", "同じ？")]
    [InlineData("同じ。", "同じ、")]
    [InlineData("「同じ、」", "「同じ。」")]
    public async Task Changed_words_internal_quotes_and_sentence_punctuation_still_require_new_analysis(string first, string next)
    {
        var analyzer = new ContentAnalyzer();
        var orchestrator = Create(analyzer);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line(first)]);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line(next)]);
        Assert.Equal(2, analyzer.CallCount);
    }

    [Fact]
    public async Task An_in_flight_sentence_is_shared_when_only_its_boundary_quote_is_added()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var first = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        var next = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("「同じ。")]);
        try { Assert.Single(analyzer.Requests); }
        finally
        {
            analyzer.Release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                [new ParsedPhraseGroup("first", "grammar", [[analyzer.Requests[0].Tokens[0].Id]], "label", "meaning", "grammar")]));
        }
        var runs = await Task.WhenAll(first, next);
        Assert.Equal(Assert.Single(runs[0].Groups).SessionGroupId, Assert.Single(runs[1].Groups).SessionGroupId);
        Assert.Equal(10, Assert.Single(Assert.Single(Assert.Single(runs[1].Groups).Parts).Rects).X);
    }

    private sealed class ContentAnalyzer(bool includeQuotes = false, bool repeatFirstToken = false) : ILlmPhraseAnalyzer
    {
        public int CallCount { get; private set; }
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var ids = includeQuotes ? request.Tokens.Select(token => token.Id).ToArray()
                : request.Tokens.Where(token => !token.Token.Surface.All(c => "「」『』".Contains(c)))
                    .Take(2).Select(token => token.Id).ToArray();
            if (repeatFirstToken) ids = [ids[0], .. ids];
            return Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                [new ParsedPhraseGroup("content", "grammar", [ids], "label", "meaning", "grammar")]));
        }
    }

    [Fact]
    public async Task A_cancelled_request_is_not_shared_when_reading_resumes_before_its_transport_finishes()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        using var cancellation = new CancellationTokenSource();
        var first = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")], cancellation.Token);
        cancellation.Cancel();
        var resumed = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        try { Assert.Equal(2, analyzer.Requests.Count); }
        finally { analyzer.Release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, [])); }

        Assert.Equal(PhraseAnalysisOutcome.Cancelled, (await first).Outcome);
        Assert.Equal(PhraseAnalysisOutcome.Success, (await resumed).Outcome);
    }

    [Fact]
    public async Task Concurrent_analyses_share_a_matching_request_and_rebind_its_current_line_ids()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var first = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ句。")]);
        var second = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ"), Line("句。", 30)]);
        try { Assert.Single(analyzer.Requests); }
        finally
        {
            analyzer.Release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                [new ParsedPhraseGroup("last", "grammar", [[analyzer.Requests[0].Tokens[^1].Id]], "label", "meaning", "grammar")]));
        }
        await first;
        var run = await second;
        Assert.Equal(new ScreenRect(10, 30, 10, 20), Assert.Single(Assert.Single(Assert.Single(run.Groups).Parts).Rects));
    }

    [Fact]
    public async Task Provider_concurrency_is_bounded_across_overlapping_batches()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var first = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("一。"), Line("二。"), Line("三。")]);
        var second = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("四。"), Line("五。"), Line("六。")]);
        try { Assert.Equal(4, analyzer.Requests.Count); }
        finally { analyzer.Release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, [])); }
        await Task.WhenAll(first, second);
        Assert.Equal(6, analyzer.Requests.Count);
    }

    [Fact]
    public async Task Cancelling_a_shared_waiter_does_not_cancel_the_provider_owner()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var owner = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ句。")]);
        using var cancellation = new CancellationTokenSource();
        var waiter = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ句。")], cancellation.Token);
        cancellation.Cancel();
        try
        {
            Assert.Equal(PhraseAnalysisOutcome.Cancelled, (await waiter).Outcome);
            Assert.False(owner.IsCompleted);
            Assert.Single(analyzer.Requests);
        }
        finally
        {
            analyzer.Release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await owner;
        }

        Assert.Equal(PhraseAnalysisOutcome.Success, (await owner).Outcome);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ句。")]);
        Assert.Single(analyzer.Requests);
    }

    [Fact]
    public async Task Clearing_the_cache_prevents_a_late_request_from_repopulating_it()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var first = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("旧句。")]);
        orchestrator.ClearCache();
        analyzer.Release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        await first;

        Assert.Empty(orchestrator.GetCachedAnalysis(Guid.NewGuid(), [Line("旧句。")]).SuccessfulSegmentIds);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("旧句。")]);
        Assert.Equal(2, analyzer.Requests.Count);
    }

    [Fact]
    public async Task A_late_result_from_a_cleared_generation_cannot_seed_current_group_identities()
    {
        var analyzer = new PendingAnalyzer();
        var orchestrator = Create(analyzer);
        var pending = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("旧句。")]);
        orchestrator.ClearCache();
        analyzer.Release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
            [new ParsedPhraseGroup("group", "grammar", [[analyzer.Requests[0].Tokens[0].Id]], "label", "meaning", "grammar")]));
        var late = await pending;

        var current = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("旧句。")]);
        var reused = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("旧句。")]);

        Assert.NotEqual(Assert.Single(late.Groups).SessionGroupId, Assert.Single(current.Groups).SessionGroupId);
        Assert.Equal(Assert.Single(current.Groups).SessionGroupId, Assert.Single(reused.Groups).SessionGroupId);
        Assert.Equal(2, analyzer.Requests.Count);
    }

    private sealed class PendingAnalyzer : ILlmPhraseAnalyzer
    {
        public List<PhraseAnalysisRequest> Requests { get; } = [];
        public TaskCompletionSource<PhraseAnalysisResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            lock (Requests) Requests.Add(request);
            return Release.Task;
        }
    }

    [Fact]
    public async Task An_in_flight_result_is_cached_under_its_request_mode_after_the_setting_changes()
    {
        var preferences = new SentenceTranslationPreferences(new TranslationSettings());
        var analyzer = new PendingTranslationAnalyzer();
        var orchestrator = new PhraseAnalysisOrchestrator(analyzer, tokenBuilder: new SentenceTokenBuilder(new CharTokenizer()),
            sentenceTranslation: preferences);
        var pending = orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        preferences.Enabled = false;
        analyzer.Release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []) { Translation = "Translation" });
        Assert.True(Assert.Single((await pending).Sentences).Requested);
        var disabled = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        Assert.Null(Assert.Single(disabled.Sentences).Translation);
        preferences.Enabled = true;
        var enabled = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        Assert.Equal("Translation", Assert.Single(enabled.Sentences).Translation);
        Assert.Equal(2, analyzer.Calls);
    }

    private sealed class PendingTranslationAnalyzer : ILlmPhraseAnalyzer
    {
        public TaskCompletionSource<PhraseAnalysisResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Release.Task;
        }
    }

    [Fact]
    public void Repeated_words_use_their_own_sentences_and_reprojection_preserves_translation()
    {
        var token = new Token("彼", "彼", "彼", "かれ", "かれ", "かれ", new PartsOfSpeech("pronoun", "", "", ""), "", "", "", 0);
        var first = new GroupedWord(token, new ScreenRect(10, 10, 20, 20)).WithSegmentId("first");
        var second = new GroupedWord(token, new ScreenRect(10, 60, 20, 20)).WithSegmentId("second");
        var sentences = new[] {
            new SentenceTranslation("first", "彼は来ない。", "English", true, "He will not come."),
            new SentenceTranslation("second", "彼は来た。", "English", true, "He came.")
        };
        var target = new WindowTarget((nint)42, "Target", new ScreenRect(0, 0, 500, 300));
        var session = WordOverlaySession.Start(target, [first, second], sentences: sentences);
        Assert.Equal("He will not come.", session.GetSentenceTranslation(first.SegmentId)?.Translation);
        Assert.Equal("He came.", session.GetSentenceTranslation(second.SegmentId)?.Translation);
        Assert.Null(session.GetSentenceTranslation(null));
        Assert.Null(session.GetSentenceTranslation("unknown"));
        var moved = session.Reproject(new WindowTarget((nint)42, "Target", new ScreenRect(100, 100, 1000, 600)));
        Assert.Same(sentences[1], moved.GetSentenceTranslation(second.SegmentId));
    }

    [Fact]
    public async Task Translation_mode_has_separate_cached_results_and_never_leaks_disabled_output()
    {
        var preferences = new SentenceTranslationPreferences(new TranslationSettings());
        var analyzer = new TranslationAnalyzer();
        var orchestrator = new PhraseAnalysisOrchestrator(analyzer, tokenBuilder: new SentenceTokenBuilder(new CharTokenizer()),
            sentenceTranslation: preferences);
        preferences.Enabled = false;
        var disabled = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        Assert.False(Assert.Single(disabled.Sentences).Requested);
        Assert.Null(Assert.Single(disabled.Sentences).Translation);
        preferences.Enabled = true;
        var enabled = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        Assert.Equal("Translation", Assert.Single(enabled.Sentences).Translation);
        preferences.Enabled = false;
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        Assert.Equal(2, analyzer.CallCount);
    }

    [Fact]
    public async Task Rewrapped_sentence_rebinds_translation_and_group_to_the_current_segment()
    {
        var analyzer = new TranslationAnalyzer();
        var orchestrator = Create(analyzer);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ句。")]);
        var second = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ"), Line("句。", 30)]);
        var sentence = Assert.Single(second.Sentences);
        Assert.Equal("s0-1", sentence.SegmentId);
        Assert.Equal("同じ句。", sentence.SourceText);
        Assert.Equal("Translation", sentence.Translation);
        Assert.Equal(sentence.SegmentId, Assert.Single(second.Groups).SegmentId);
        Assert.Equal(1, analyzer.CallCount);
    }

    [Fact]
    public async Task Missing_translation_does_not_repeat_a_successful_request()
    {
        var analyzer = new CountingAnalyzer();
        var orchestrator = Create(analyzer);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        var cached = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        Assert.True(cached.Succeeded);
        Assert.Null(Assert.Single(cached.Sentences).Translation);
        Assert.Equal(1, analyzer.CallCount);
    }

    private sealed class TranslationSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }

    private sealed class TranslationAnalyzer : ILlmPhraseAnalyzer
    {
        public int CallCount { get; private set; }
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                [new ParsedPhraseGroup("group", "grammar", [[request.Tokens[0].Id]], "label", "meaning", "grammar")])
                { Translation = "Translation" });
        }
    }

    [Fact]
    public async Task Logs_cache_hits_and_new_provider_requests_with_the_recognition_id()
    {
        var logger = new RecordingLogger();
        var orchestrator = new PhraseAnalysisOrchestrator(new CountingAnalyzer(), new SentenceSegmenter(),
            new SentenceTokenBuilder(new CharTokenizer()), logger: logger);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("旧句。")]);
        var recognitionId = Guid.NewGuid();

        await orchestrator.AnalyzeAsync(recognitionId, [Line("旧句。"), Line("新句。", 60)]);

        Assert.Contains($"[PHRASE] Recognition {recognitionId:N}: segments=2, cacheHits=1, providerRequests=1.", logger.Messages);
    }

    [Fact]
    public async Task Same_text_uses_separate_results_for_each_language()
    {
        var analyzer = new CountingAnalyzer();
        var language = new LanguagePreferences();
        var orchestrator = new PhraseAnalysisOrchestrator(analyzer, new SentenceSegmenter(),
            new SentenceTokenBuilder(new CharTokenizer()), language);

        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        language.TrySave("French");
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);

        Assert.Equal(2, analyzer.CallCount);
        Assert.Equal("French", analyzer.LastLanguage);
    }

    [Fact]
    public async Task Reuses_a_successful_result_for_the_same_segment_text()
    {
        var analyzer = new CountingAnalyzer();
        var orchestrator = Create(analyzer);

        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ。")]);

        Assert.Equal(1, analyzer.CallCount);
    }

    [Fact]
    public async Task Does_not_cache_failed_results()
    {
        var analyzer = new SequencedAnalyzer(
            new PhraseAnalysisResult(PhraseAnalysisOutcome.NoKey, []),
            new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        var orchestrator = Create(analyzer);

        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("失敗。")]);
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("失敗。")]);

        Assert.Equal(2, analyzer.CallCount);
    }

    [Fact]
    public async Task Evicts_the_oldest_result_when_the_cache_is_full()
    {
        var analyzer = new CountingAnalyzer();
        var orchestrator = Create(analyzer);

        for (var i = 0; i < 51; i++)
            await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line($"句{i}。")] );
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("句0。")] );

        Assert.Equal(52, analyzer.CallCount);
    }

    [Fact]
    public async Task Keeps_cached_results_when_a_fresh_result_evicts_one_during_merge()
    {
        var analyzer = new CountingAnalyzer();
        var orchestrator = Create(analyzer);

        for (var i = 0; i < 50; i++)
            await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line($"句{i}。")]);

        var run = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("句0。"), Line("新句。")] );

        Assert.Equal(PhraseAnalysisOutcome.Success, run.Outcome);
        Assert.Equal(51, analyzer.CallCount);
    }

    [Fact]
    public async Task ClearCache_forces_the_next_analysis_to_call_the_provider()
    {
        var analyzer = new CountingAnalyzer();
        var orchestrator = Create(analyzer);

        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("消去。")]);
        orchestrator.ClearCache();
        await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("消去。")]);

        Assert.Equal(2, analyzer.CallCount);
    }

    [Fact]
    public async Task Reused_groups_keep_their_session_identity()
    {
        var analyzer = new GroupAnalyzer();
        var orchestrator = Create(analyzer);

        var first = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("安定。")]);
        var second = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("安定。")]);

        Assert.Equal(Assert.Single(first.Groups).SessionGroupId, Assert.Single(second.Groups).SessionGroupId);
    }

    [Fact]
    public async Task Clearing_the_cache_also_resets_materialized_group_identity()
    {
        var orchestrator = Create(new GroupAnalyzer());
        var first = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("安定。")]);

        orchestrator.ClearCache();
        var next = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("安定。")]);

        Assert.NotEqual(Assert.Single(first.Groups).SessionGroupId, Assert.Single(next.Groups).SessionGroupId);
    }

    [Fact]
    public async Task Evicting_a_sentence_also_forgets_its_materialized_group_identity()
    {
        var orchestrator = Create(new GroupAnalyzer());
        var first = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("句0。")]);
        for (var index = 1; index <= 50; index++)
            await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line($"句{index}。")]);

        var next = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("句0。")]);

        Assert.NotEqual(Assert.Single(first.Groups).SessionGroupId, Assert.Single(next.Groups).SessionGroupId);
    }

    [Fact]
    public async Task Rewrapped_text_reuses_analysis_and_maps_cached_group_ids_to_current_tokens()
    {
        var analyzer = new LastTokenAnalyzer();
        var orchestrator = Create(analyzer);
        var first = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ句。")]);

        var second = await orchestrator.AnalyzeAsync(Guid.NewGuid(), [Line("同じ"), Line("句。", 30)]);

        Assert.Equal(1, analyzer.CallCount);
        var firstGroup = Assert.Single(first.Groups);
        var secondGroup = Assert.Single(second.Groups);
        Assert.Equal(firstGroup.SessionGroupId, secondGroup.SessionGroupId);
        var part = Assert.Single(secondGroup.Parts);
        Assert.Equal("。", part.Surface);
        Assert.Equal(new ScreenRect(10, 30, 10, 20), Assert.Single(part.Rects));
        Assert.Null(second.Warning);
    }

    private static PhraseAnalysisOrchestrator Create(ILlmPhraseAnalyzer analyzer)
        => new(analyzer, new SentenceSegmenter(), new SentenceTokenBuilder(new CharTokenizer()));

    private static OcrLine Line(string text, int y = 0)
        => new(text.Select((character, index) => new OcrWord(character.ToString(), new ScreenRect(index * 10, y, 10, 20))).ToArray());

    private sealed class LastTokenAnalyzer : ILlmPhraseAnalyzer
    {
        public int CallCount { get; private set; }

        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                [new ParsedPhraseGroup("last", "grammar", [[request.Tokens[^1].Id]], "label", "meaning", "grammar")]));
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
            => Messages.Add(string.Format(CultureInfo.InvariantCulture, message, args));
    }

    private sealed class CountingAnalyzer : ILlmPhraseAnalyzer
    {
        public int CallCount { get; private set; }
        public string? LastLanguage { get; private set; }

        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastLanguage = request.ExplanationLanguage;
            return Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        }
    }

    private sealed class LanguagePreferences : IExplanationLanguagePreferences
    {
        public string? CustomLanguage { get; private set; }
        public string EffectiveLanguage => CustomLanguage ?? "English";
        public event EventHandler? EffectiveLanguageChanged;
        public bool TrySave(string? language)
        {
            CustomLanguage = language;
            EffectiveLanguageChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    private sealed class SequencedAnalyzer(params PhraseAnalysisResult[] results) : ILlmPhraseAnalyzer
    {
        private int _index;

        public int CallCount => _index;

        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(results[Math.Min(_index++, results.Length - 1)]);
    }

    private sealed class GroupAnalyzer : ILlmPhraseAnalyzer
    {
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PhraseAnalysisResult(
                PhraseAnalysisOutcome.Success,
                [new ParsedPhraseGroup("same", "grammar", [[request.Tokens[0].Id]], "label", "meaning", "grammar")]));
    }

    private sealed class CharTokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text)
            => (text ?? string.Empty)
                .Select((character, index) => new Token(
                    character.ToString(), character.ToString(), character.ToString(), character.ToString(), character.ToString(), character.ToString(),
                    new PartsOfSpeech("", "", "", ""), "", "", "", index))
                .ToArray();
    }
}

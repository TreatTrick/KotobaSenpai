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

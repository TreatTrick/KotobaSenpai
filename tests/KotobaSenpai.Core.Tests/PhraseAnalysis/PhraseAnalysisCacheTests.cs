using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Logging;
using System.Globalization;

namespace KotobaSenpai.Core.Tests.PhraseAnalysis;

public sealed class PhraseAnalysisCacheTests
{
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

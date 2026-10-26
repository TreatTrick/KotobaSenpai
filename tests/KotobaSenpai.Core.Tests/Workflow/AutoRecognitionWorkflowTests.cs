using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Workflow;

namespace KotobaSenpai.Core.Tests.Workflow;

public sealed class AutoRecognitionWorkflowTests
{
    [Fact]
    public async Task Changing_language_keeps_the_overlay_and_reanalyzes_unchanged_text_next_time()
    {
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("同じ。")], [Line("同じ。")]);
        var analyzer = new CountingAnalyzer();
        var language = new LanguagePreferences();
        using var service = CreateService(recognizer, overlay, true, analyzer, language);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession;

        language.TrySave("French");

        Assert.Same(previous, overlay.LastSession);
        Assert.Equal(1, analyzer.CallCount);
        await service.RecognizeChangedAndShowAsync(Target());
        Assert.Equal(2, analyzer.CallCount);
        Assert.Equal("French", analyzer.LastLanguage);
    }

    [Fact]
    public async Task Cancelled_automatic_analysis_cannot_publish_a_late_result()
    {
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new RecordingOverlay();
        var service = CreateService(new SequencedRecognizer([Line("旧句。")], [Line("旧句。新句。")]),
            overlay, phraseAnalysisEnabled: true, new SequencedAnalyzer(release));
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession;
        using var cancellation = new CancellationTokenSource();
        var pending = service.RecognizeChangedAndShowAsync(Target(), cancellation.Token);
        cancellation.Cancel();
        release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Same(previous, overlay.LastSession);
    }

    [Fact]
    public async Task Unchanged_ocr_text_does_not_refresh_the_overlay()
    {
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("同じ。")], [Line("同じ。")]);
        var analyzer = new CountingAnalyzer();
        var service = CreateService(recognizer, overlay, phraseAnalysisEnabled: true, analyzer);

        await service.RecognizeAndShowAsync(Target());
        await service.RecognizeChangedAndShowAsync(Target());

        Assert.Equal(2, overlay.ShowCount);
        Assert.Equal(1, overlay.HideCount);
        Assert.Equal(1, analyzer.CallCount);
    }

    [Fact]
    public async Task Incremental_recognition_keeps_the_existing_overlay_until_analysis_finishes()
    {
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("旧句。")], [Line("旧句。新句。")] );
        var analyzer = new SequencedAnalyzer(release);
        var service = CreateService(recognizer, overlay, phraseAnalysisEnabled: true, analyzer);

        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession;
        var incremental = service.RecognizeChangedAndShowAsync(Target());

        await Task.Delay(20);

        Assert.Same(previous, overlay.LastSession);
        Assert.Equal(1, overlay.HideCount);

        release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        await incremental;

        Assert.NotSame(previous, overlay.LastSession);
        Assert.Equal(3, overlay.ShowCount);
    }

    [Fact]
    public async Task Full_change_clears_cached_results_before_reanalyzing()
    {
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("甲。"), Line("乙。")], [Line("甲。")]);
        var analyzer = new CountingAnalyzer();
        var service = CreateService(recognizer, overlay, phraseAnalysisEnabled: true, analyzer);

        await service.RecognizeAndShowAsync(Target());
        await service.RecognizeChangedAndShowAsync(Target());

        Assert.Equal(3, analyzer.CallCount);
    }

    private static WordOverlayApplicationService CreateService(
        IWindowWordRecognizer recognizer,
        IOverlayRenderer overlay,
        bool phraseAnalysisEnabled,
        ILlmPhraseAnalyzer? analyzer = null,
        IExplanationLanguagePreferences? language = null)
        => new(
            recognizer,
            new WordGroupingService(new CharTokenizer(), new SentenceSegmenter(), new IdentitySpanResolver()),
            overlay,
            phraseOrchestrator: analyzer is null
                ? null
                : new PhraseAnalysisOrchestrator(analyzer, new SentenceSegmenter(), new SentenceTokenBuilder(new CharTokenizer()), language),
            settings: new FakeSettings(phraseAnalysisEnabled),
            explanationLanguage: language);

    private static WindowTarget Target() => new((nint)42, "VN", new ScreenRect(0, 0, 200, 100));

    private static OcrLine Line(string text)
        => new(text.Select((character, index) => new OcrWord(character.ToString(), new ScreenRect(index * 10, 0, 10, 20))).ToArray());

    private sealed class SequencedRecognizer : IWindowWordRecognizer
    {
        private readonly IReadOnlyList<OcrLine>[] _results;
        private int _index;

        public SequencedRecognizer(params IReadOnlyList<OcrLine>[] results) => _results = results;

        public Task<WordRecognitionResult> RecognizeAsync(
            Guid recognitionId,
            WindowTarget target,
            CancellationToken cancellationToken = default,
            ScreenRect? region = null)
        {
            var lines = _results[Math.Min(_index++, _results.Length - 1)];
            return Task.FromResult(new WordRecognitionResult(100, 50, lines));
        }
    }

    private sealed class RecordingOverlay : IOverlayRenderer
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public WordOverlaySession? LastSession { get; private set; }

        public void Show(WordOverlaySession session, Func<bool>? canRender = null)
        {
            ShowCount++;
            LastSession = session;
        }

        public void Hide()
        {
            HideCount++;
            LastSession = null;
        }
    }

    private sealed class SequencedAnalyzer : ILlmPhraseAnalyzer
    {
        private readonly TaskCompletionSource<PhraseAnalysisResult> _release;
        private int _calls;

        public SequencedAnalyzer(TaskCompletionSource<PhraseAnalysisResult> release) => _release = release;

        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _calls) == 1
                ? Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []))
                : _release.Task;
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

    private sealed class FakeSettings(bool phraseAnalysisEnabled) : ISettingsService
    {
        public string? GetValue(string key)
            => key == "PhraseGroupsEnabled" && phraseAnalysisEnabled ? "true" : null;

        public void SetValue(string key, string? value) { }
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

    private sealed class IdentitySpanResolver : ITokenSpanResolver
    {
        public IReadOnlyList<LookupSpan> Resolve(IReadOnlyList<Token> tokens)
            => tokens.Select(token => new LookupSpan([token], token.Surface, [])).ToArray();
    }
}

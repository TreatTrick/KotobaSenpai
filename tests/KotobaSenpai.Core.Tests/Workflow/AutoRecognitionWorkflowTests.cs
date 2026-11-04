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
        using var service = CreateService(recognizer, overlay, analyzer, language);
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
            overlay, new SequencedAnalyzer(release));
        await service.RecognizeAndShowAsync(Target());
        using var cancellation = new CancellationTokenSource();
        var pending = service.RecognizeChangedAndShowAsync(Target(), cancellation.Token);
        var preview = overlay.LastSession;
        cancellation.Cancel();
        release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Same(preview, overlay.LastSession);
    }

    [Fact]
    public async Task Unchanged_ocr_text_does_not_refresh_the_overlay()
    {
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("同じ。")], [Line("同じ。")]);
        var analyzer = new CountingAnalyzer();
        var service = CreateService(recognizer, overlay, analyzer);

        await service.RecognizeAndShowAsync(Target());
        await service.RecognizeChangedAndShowAsync(Target());

        Assert.Equal(2, overlay.ShowCount);
        Assert.Equal(1, overlay.HideCount);
        Assert.Equal(1, analyzer.CallCount);
    }

    [Fact]
    public async Task Incremental_recognition_keeps_cached_annotations_while_new_analysis_is_pending()
    {
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("旧句。")], [Line("旧句。"), Line("新句。", 60)]);
        var analyzer = new SequencedAnalyzer(release);
        var service = CreateService(recognizer, overlay, analyzer);

        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession;
        var incremental = service.RecognizeChangedAndShowAsync(Target());

        Assert.NotNull(overlay.LastSession);
        Assert.Contains("s0-0", overlay.LastSession.UnderlineSegmentIds!);
        Assert.DoesNotContain("s1-1", overlay.LastSession.UnderlineSegmentIds!);
        Assert.Contains(overlay.LastSession.Words, word => word.Token.Surface == "新");
        Assert.Equal(1, overlay.HideCount);

        release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        await incremental;

        Assert.NotSame(previous, overlay.LastSession);
        Assert.Equal(4, overlay.ShowCount);
    }

    [Fact]
    public async Task Removing_a_sentence_reuses_the_remaining_sentence()
    {
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("甲。"), Line("乙。")], [Line("甲。")]);
        var analyzer = new CountingAnalyzer();
        var service = CreateService(recognizer, overlay, analyzer);

        await service.RecognizeAndShowAsync(Target());
        await service.RecognizeChangedAndShowAsync(Target());

        Assert.Equal(2, analyzer.CallCount);
        Assert.Equal(1, overlay.HideCount);
        Assert.DoesNotContain(overlay.LastSession!.Words, word => word.Token.Surface == "乙");
    }

    [Fact]
    public async Task Corrected_partial_text_and_added_ruby_do_not_clear_unchanged_annotations_or_resend_old_segments()
    {
        var oldLines = new[]
        {
            Line("分からない事は苛立たしい。自分の感情なら尚更だ。"),
            Line("完璧主義者である彼女にとって、それは痛くはないけど気", 30),
            Line("がかりなトゲのようなもので、", 60),
            Line("「いやぁ、ははは。ほら蒼崎くん、挨拶してあげなくちゃ」", 120),
        };
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer(
            [.. oldLines, Line("山城の場を草", 200)],
            [.. oldLines, Line("つくろ", 180), Line("山城の場を取り繕う声にも、わりと本気でイラっとした。", 200)]),
            overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession!;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, overlay.HideCount);
            var preview = overlay.LastSession!;
            Assert.Equal(3, preview.PhraseGroups.Count);
            Assert.Equal(previous.PhraseGroups.Take(3).Select(group => group.SessionGroupId),
                preview.PhraseGroups.Select(group => group.SessionGroupId));
            Assert.Equal(previous.WordMeanings.Take(3).Select(word => (word.Headword, word.Reading, word.Pos, word.Meaning, word.Grammar)),
                preview.WordMeanings.Select(word => (word.Headword, word.Reading, word.Pos, word.Meaning, word.Grammar)));
            Assert.Equal(previous.Lines.Where(line => line.Y < 160), preview.Lines);
            Assert.Contains("s0-0", preview.UnderlineSegmentIds!);
            Assert.Contains("s1-2", preview.UnderlineSegmentIds!);
            Assert.Contains("s3-3", preview.UnderlineSegmentIds!);
            Assert.DoesNotContain("s4-5", preview.UnderlineSegmentIds!);
            Assert.Equal(5, analyzer.Requests.Count);
            Assert.Single(analyzer.Requests, request => request.SegmentText == oldLines[0].Text);
            Assert.Single(analyzer.Requests, request => request.SegmentText == oldLines[1].Text + oldLines[2].Text);
            Assert.Single(analyzer.Requests, request => request.SegmentText == oldLines[3].Text);
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }

        Assert.Contains("s4-5", overlay.LastSession!.UnderlineSegmentIds!);
        Assert.Equal(5, analyzer.Requests.Count);
    }

    [Fact]
    public async Task Unrelated_replacement_removes_stale_annotations_while_new_analysis_is_pending()
    {
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line("旧句。")], [Line("別文。")]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            Assert.Empty(overlay.LastSession!.PhraseGroups);
            Assert.Empty(overlay.LastSession.Lines);
            Assert.DoesNotContain(overlay.LastSession.Words, word => word.Token.Surface == "旧");
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
    }

    private static WordOverlayApplicationService CreateService(
        IWindowWordRecognizer recognizer,
        IOverlayRenderer overlay,
        ILlmPhraseAnalyzer analyzer,
        IExplanationLanguagePreferences? language = null)
        => new(
            recognizer,
            new WordGroupingService(new CharTokenizer(), new SentenceSegmenter(), new IdentitySpanResolver()),
            overlay,
            phraseOrchestrator: new PhraseAnalysisOrchestrator(analyzer, new SentenceSegmenter(),
                new SentenceTokenBuilder(new CharTokenizer(), new IdentitySpanResolver()), language),
            explanationLanguage: language);

    private static WindowTarget Target() => new((nint)42, "VN", new ScreenRect(0, 0, 1000, 500));

    private static OcrLine Line(string text, int y = 0)
        => new(text.Select((character, index) => new OcrWord(character.ToString(), new ScreenRect(index * 10, y, 10, 20))).ToArray());

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
            return Task.FromResult(new WordRecognitionResult(1000, 500, lines));
        }
    }

    private sealed class RecordingOverlay : IOverlayRenderer
    {
        public void Suspend() { }
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

    private sealed class RecordingAnalyzer(TaskCompletionSource<PhraseAnalysisResult> release) : ILlmPhraseAnalyzer
    {
        public List<PhraseAnalysisRequest> Requests { get; } = [];
        public bool DelayNewRequests { get; set; }

        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return DelayNewRequests
                ? release.Task
                : Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                    [new ParsedPhraseGroup("first", "grammar", [[request.Tokens[0].Id]], "label", "meaning", "grammar")])
                {
                    Words = [new ParsedWordMeaning(request.Tokens[0].Token.Surface, "noun", "meaning", "grammar")],
                });
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

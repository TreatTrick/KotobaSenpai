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

public sealed class RecognitionSessionTests
{
    private static readonly WindowTarget Target = new((nint)42, "Reading target", new(0, 0, 1000, 500));

    [Fact]
    public async Task Automatic_sampling_returns_before_the_model_and_pause_cancels_all_tracked_analysis()
    {
        var recognizer = new Recognizer();
        var analyzer = new Analyzer();
        var overlay = new Overlay();
        var automatic = new Automatic();
        using var workflow = CreateWorkflow(recognizer, analyzer, overlay);
        using var session = new RecognitionSessionController(workflow, new Settings(), automatic);
        await session.RecognizeAsync(Target);
        await session.SetAutoRecognitionAsync(true, Target);
        analyzer.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        recognizer.Text = "ChangedSentence.";

        await automatic.TriggerAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, analyzer.Calls);
        var preview = overlay.Saved;
        session.PauseAndHide();
        Assert.True(analyzer.LastToken.IsCancellationRequested);
        var stop = session.StopAsync();
        Assert.False(stop.IsCompleted);
        analyzer.Pending.SetResult(new(PhraseAnalysisOutcome.Success, []));
        await stop.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Null(overlay.Saved);
        Assert.NotNull(preview);
    }

    [Fact]
    public async Task Pausing_a_partial_batch_keeps_completed_sentences_and_retries_only_unfinished_work()
    {
        var analyzer = new Analyzer
        {
            ImmediatelyComplete = "FirstSentence.",
            Pending = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var recognizer = new Recognizer { Text = "FirstSentence.\nSecondSentence." };
        using var workflow = CreateWorkflow(recognizer, analyzer, new Overlay());
        using var session = new RecognitionSessionController(workflow, new Settings());
        var pending = session.RecognizeAsync(Target);
        Assert.Equal(2, analyzer.Calls);
        session.PauseAndHide();
        analyzer.Pending.SetResult(new(PhraseAnalysisOutcome.Success, []));
        await pending;
        await session.ResumeAsync(Target);
        Assert.Equal(RecognitionPhase.Ready, session.State.Phase);
        Assert.Equal(3, analyzer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restoring_an_unavailable_target_does_no_OCR_and_stays_paused(bool minimized)
    {
        var recognizer = new Recognizer();
        var analyzer = new Analyzer();
        var overlay = new Overlay();
        var tracker = new Tracker();
        tracker.Attach(Target);
        using var workflow = CreateWorkflow(recognizer, analyzer, overlay, tracker);
        using var session = new RecognitionSessionController(workflow, new Settings());
        await session.RecognizeAsync(Target);
        session.PauseAndHide();
        tracker.Snapshot = tracker.Snapshot! with { IsMinimized = minimized, IsOccluded = !minimized };
        await session.ResumeAsync(Target);
        Assert.Equal(RecognitionPhase.Hidden, session.State.Phase);
        Assert.Equal(1, recognizer.Calls);
        Assert.Equal(1, analyzer.Calls);
        tracker.Snapshot = tracker.Snapshot with { IsMinimized = false, IsOccluded = false, Bounds = new(50, 80, 800, 400) };
        await session.ResumeAsync(Target);
        Assert.Equal(RecognitionPhase.Ready, session.State.Phase);
        Assert.Equal(tracker.Snapshot.Target, overlay.Saved!.Target);
        Assert.Equal(1, analyzer.Calls);
    }

    [Fact]
    public async Task Pausing_a_batch_prevents_queued_sentences_from_reaching_the_provider()
    {
        var analyzer = new Analyzer { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var recognizer = new Recognizer { Text = string.Join('\n', Enumerable.Range(0, 8).Select(i => "Sentence" + i)) };
        using var workflow = CreateWorkflow(recognizer, analyzer, new Overlay());
        using var session = new RecognitionSessionController(workflow, new Settings());
        var pending = session.RecognizeAsync(Target);
        Assert.Equal(4, analyzer.Calls);
        session.PauseAndHide();
        analyzer.Pending.SetResult(new(PhraseAnalysisOutcome.Success, []));
        await pending;
        Assert.Equal(4, analyzer.Calls);
        Assert.Equal(RecognitionPhase.Hidden, session.State.Phase);
    }

    [Fact]
    public async Task A_failed_restore_keeps_saved_results_and_a_later_empty_frame_is_safe()
    {
        var recognizer = new Recognizer();
        var overlay = new Overlay();
        var analyzer = new Analyzer();
        using var workflow = CreateWorkflow(recognizer, analyzer, overlay);
        using var session = new RecognitionSessionController(workflow, new Settings());
        await session.RecognizeAsync(Target);
        var saved = overlay.Saved;
        session.PauseAndHide();
        recognizer.Fail = true;
        await session.ResumeAsync(Target);
        Assert.Equal(RecognitionPhase.Hidden, session.State.Phase);
        Assert.NotNull(session.State.Error);
        Assert.Same(saved, overlay.Saved);
        recognizer.Fail = false;
        recognizer.Text = "";
        await session.ResumeAsync(Target);
        Assert.Equal(RecognitionPhase.Ready, session.State.Phase);
        Assert.Equal(0, session.State.WordCount);
        Assert.Equal(1, analyzer.Calls);
    }

    [Fact]
    public async Task Hidden_sessions_do_no_work_and_resume_reuses_analysis_on_the_current_frame()
    {
        var recognizer = new Recognizer();
        var analyzer = new Analyzer();
        var overlay = new Overlay();
        var automatic = new Automatic();
        using var workflow = CreateWorkflow(recognizer, analyzer, overlay);
        using var session = new RecognitionSessionController(workflow, new Settings(), automatic);
        await session.RecognizeAsync(Target);
        Assert.Null(session.State.Error);
        await session.SetAutoRecognitionAsync(true, Target);
        var saved = overlay.Saved;

        session.PauseAndHide();
        await automatic.TriggerAsync();
        await session.RecognizeAsync(Target);
        await session.SetAutoRecognitionAsync(true, Target);

        Assert.Equal(RecognitionPhase.Hidden, session.State.Phase);
        Assert.False(automatic.IsRunning);
        Assert.Same(saved, overlay.Saved);
        Assert.Equal(1, recognizer.Calls);
        Assert.Equal(1, analyzer.Calls);
        await session.ResumeAsync(Target);
        Assert.Equal(2, recognizer.Calls);
        Assert.Equal(1, analyzer.Calls);
        Assert.True(automatic.IsRunning);

        session.PauseAndHide();
        recognizer.Text = "ChangedSentence.";
        await session.SetAutoRecognitionAsync(false, Target);
        await session.ResumeAsync(Target);
        Assert.Equal(2, analyzer.Calls);
        Assert.False(automatic.IsRunning);
        session.Stop();
        await session.ResumeAsync(Target);
        Assert.Null(overlay.Saved);
        Assert.Equal(3, recognizer.Calls);
    }

    [Fact]
    public async Task Pause_rejects_late_analysis_and_resume_retries_only_the_missing_result()
    {
        var analyzer = new Analyzer { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var overlay = new Overlay();
        using var workflow = CreateWorkflow(new Recognizer(), analyzer, overlay);
        using var session = new RecognitionSessionController(workflow, new Settings());
        var pending = session.RecognizeAsync(Target);
        Assert.Null(session.State.Error);
        var saved = overlay.Saved;
        session.PauseAndHide();
        analyzer.Pending.SetResult(new(PhraseAnalysisOutcome.Success, []));
        await pending;

        Assert.Equal(RecognitionPhase.Hidden, session.State.Phase);
        Assert.Same(saved, overlay.Saved);
        Assert.True(analyzer.LastToken.IsCancellationRequested);
        await session.ResumeAsync(Target);
        Assert.Equal(2, analyzer.Calls);
        Assert.Equal(RecognitionPhase.Ready, session.State.Phase);
    }

    [Fact]
    public async Task Clearing_phrase_cache_prevents_an_old_recognition_from_replacing_its_preview()
    {
        var analyzer = new Analyzer { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var overlay = new Overlay();
        var phraseOrchestrator = new PhraseAnalysisOrchestrator(analyzer,
            tokenBuilder: new SentenceTokenBuilder(new Tokenizer()));
        using var workflow = CreateWorkflow(new Recognizer(), analyzer, overlay, phraseOrchestrator: phraseOrchestrator);

        var recognition = workflow.RecognizeAndShowAsync(Target);
        var preview = overlay.Saved;
        Assert.NotNull(preview);

        phraseOrchestrator.ClearCache();
        analyzer.Pending.SetResult(new(PhraseAnalysisOutcome.Success, []));
        await recognition;

        Assert.Same(preview, overlay.Saved);
    }

    [Fact]
    public async Task Stop_cancels_immediately_and_waits_for_work_started_outside_page_commands()
    {
        var analyzer = new Analyzer { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var recognizer = new Recognizer();
        var overlay = new Overlay();
        using var workflow = CreateWorkflow(recognizer, analyzer, overlay);
        using var session = new RecognitionSessionController(workflow, new Settings());
        var pending = session.RecognizeAsync(Target);
        await session.RecognizeAsync(Target);
        Assert.Equal(1, recognizer.Calls);
        var stopped = session.StopAsync();
        Assert.True(analyzer.LastToken.IsCancellationRequested);
        Assert.False(stopped.IsCompleted);
        analyzer.Pending.SetResult(new(PhraseAnalysisOutcome.Success, []));
        await stopped;
        await pending;
        Assert.Null(overlay.Saved);
        Assert.Equal(RecognitionPhase.Stopped, session.State.Phase);
    }

    private static WordOverlayApplicationService CreateWorkflow(Recognizer recognizer, Analyzer analyzer, Overlay overlay,
        ITargetWindowTracker? tracker = null, PhraseAnalysisOrchestrator? phraseOrchestrator = null)
        => new(recognizer, new Grouping(), overlay,
            phraseOrchestrator ?? new PhraseAnalysisOrchestrator(analyzer,
                tokenBuilder: new SentenceTokenBuilder(new Tokenizer())), tracker: tracker);

    private sealed class Tracker : ITargetWindowTracker
    {
        public TargetWindowSnapshot? Snapshot { get; set; }
        public TargetWindowSnapshot? Current => Snapshot;
        public event EventHandler<TargetWindowSnapshot>? Changed { add { } remove { } }
        public TargetWindowSnapshot Attach(WindowTarget target)
            => Snapshot = new(target.Handle, target.Title, target.Bounds, 1, true, false, true);
        public TargetWindowSnapshot Refresh() => Snapshot!;
        public void Detach() => Snapshot = null;
        public void Dispose() => Detach();
    }

    private sealed class Recognizer : IWindowWordRecognizer
    {
        public int Calls { get; private set; }
        public string Text { get; set; } = "InitialSentence.";
        public bool Fail { get; set; }
        public Task<WordRecognitionResult> RecognizeAsync(Guid id, WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidOperationException("Capture failed.");
            return Task.FromResult(new WordRecognitionResult(1000, 500,
                Text.Split('\n').Select((line, row) => new OcrLine(line.Select((c, i) => new OcrWord(c.ToString(), new(i * 10, row * 80, 10, 20))).ToArray())).ToArray()));
        }
    }

    private sealed class Analyzer : ILlmPhraseAnalyzer
    {
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public TaskCompletionSource<PhraseAnalysisResult>? Pending { get; set; }
        public string? ImmediatelyComplete { get; set; }
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            return request.SegmentText == ImmediatelyComplete
                ? Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []))
                : Pending?.Task ?? Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        }
    }

    private sealed class Tokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text) => (text ?? "").Select((c, i) => new Token(
            c.ToString(), c.ToString(), c.ToString(), c.ToString(), c.ToString(), c.ToString(),
            new PartsOfSpeech("noun", "", "", ""), "", "", "", i)).ToArray();
    }
    private sealed class Grouping : IOcrWordGroupingService
    {
        public IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines, IReadOnlyList<SentenceSegment>? segments = null) => [];
    }
    private sealed class Overlay : IOverlayRenderer
    {
        public WordOverlaySession? Saved { get; private set; }
        public void Show(WordOverlaySession session, Func<bool>? canRender = null)
        { if (canRender?.Invoke() != false) Saved = session; }
        public void Hide() => Saved = null;
        public void Suspend() { }
    }
    private sealed class Settings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }
    private sealed class Automatic : IAutoRecognitionController
    {
        private Func<CancellationToken, Task>? _callback;
        public bool IsRunning { get; private set; }
        public void Start(WindowTarget target, Func<CancellationToken, Task>? onChange = null) { IsRunning = true; _callback = onChange; }
        public Task TriggerAsync() => _callback!(CancellationToken.None);
        public void Stop() => IsRunning = false;
        public void Dispose() => Stop();
    }
}

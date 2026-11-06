using System.Threading.Channels;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Workflow;

namespace KotobaSenpai.Core.Tests.Ocr;

public sealed class StableWindowWordRecognizerTests
{
    private static readonly WindowTarget Target = new((nint)42, "Target", new(0, 0, 1000, 500));

    [Fact]
    public async Task The_recorded_typewriter_sequence_analyzes_the_complete_first_sentence_once_and_only_adds_the_second_sentence()
    {
        const string partial = "「僕は職員室にいるから、終わったらん";
        const string first = "「僕は職員室にいるから、終わったら来るように。";
        const string second = "いいかい蒼崎くん、くれぐれも、くれぐれもだぜ?";
        var clock = new ManualClock();
        var raw = new Recognizer([partial, .. Enumerable.Repeat(first, 4), first + "\n" + second]);
        var analyzer = new RecordingAnalyzer();
        using var workflow = new WordOverlayApplicationService(new StableWindowWordRecognizer(raw, clock),
            new Grouping(), new Overlay(), new PhraseAnalysisOrchestrator(analyzer, tokenBuilder: new SentenceTokenBuilder(new Tokenizer())));
        var initial = workflow.RecognizeAndShowAsync(Target);
        for (var i = 0; i < 4; i++)
        {
            Assert.Empty(analyzer.Texts);
            await clock.TickAsync();
        }
        await initial.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([first], analyzer.Texts);

        var appended = workflow.RecognizeChangedAndShowAsync(Target);
        for (var i = 0; i < 3; i++) await clock.TickAsync();
        await appended.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([first, second], analyzer.Texts);
        Assert.DoesNotContain(partial, analyzer.Texts);
    }

    [Fact]
    public async Task Continuously_changing_text_does_not_force_confirmation_after_an_arbitrary_timeout()
    {
        var clock = new ManualClock();
        var raw = new Recognizer(Enumerable.Range(0, 25).Select(i => $"途中{i}").ToArray());
        using var cancellation = new CancellationTokenSource();
        var pending = new StableWindowWordRecognizer(raw, clock).RecognizeAsync(Guid.NewGuid(), Target, cancellation.Token);
        for (var i = 0; i < 20; i++)
        {
            await clock.TickAsync();
            Assert.False(pending.IsCompleted);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task Typewriter_text_is_confirmed_only_after_the_complete_text_has_stayed_unchanged_for_600_ms()
    {
        var clock = new ManualClock();
        var raw = new Recognizer("「僕は職員室にいるから、終わったらん", "「僕は職員室にいるから、終わったら来るように。");
        var stable = new StableWindowWordRecognizer(raw, clock);
        var id = Guid.NewGuid();
        var region = new ScreenRect(0, 0, 900, 400);
        var pending = stable.RecognizeAsync(id, Target, region: region);

        for (var i = 0; i < 3; i++)
        {
            Assert.False(pending.IsCompleted);
            await clock.TickAsync();
        }
        Assert.False(pending.IsCompleted);
        await clock.TickAsync();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("「僕は職員室にいるから、終わったら来るように。", Assert.Single(result.Lines).Text);
        Assert.Equal(5, raw.Calls);
        Assert.All(raw.Ids, value => Assert.Equal(id, value));
        Assert.All(raw.Regions, value => Assert.Equal(region, value));
        Assert.Equal(5, result.Lines[0].Words[0].FrameBounds.Y);
    }

    [Fact]
    public async Task Unchanged_text_uses_the_final_sample_geometry_even_when_the_background_or_layout_changes()
    {
        var clock = new ManualClock();
        var raw = new Recognizer("同じ。");
        var stable = new StableWindowWordRecognizer(raw, clock);
        var pending = stable.RecognizeAsync(Guid.NewGuid(), Target);
        for (var i = 0; i < 3; i++) await clock.TickAsync();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, raw.Calls);
        Assert.Equal(4, result.Lines[0].Words[0].FrameBounds.Y);
    }

    [Fact]
    public async Task Cancellation_stops_confirmation_without_returning_a_partial_result()
    {
        var clock = new ManualClock();
        var raw = new Recognizer("途中");
        var stable = new StableWindowWordRecognizer(raw, clock);
        using var cancellation = new CancellationTokenSource();
        var pending = stable.RecognizeAsync(Guid.NewGuid(), Target, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, raw.Calls);
    }

    private sealed class Recognizer(params string[] text) : IWindowWordRecognizer
    {
        public int Calls { get; private set; }
        public List<Guid> Ids { get; } = [];
        public List<ScreenRect?> Regions { get; } = [];

        public Task<WordRecognitionResult> RecognizeAsync(Guid recognitionId, WindowTarget target,
            CancellationToken cancellationToken = default, ScreenRect? region = null)
        {
            var sample = text[Math.Min(Calls++, text.Length - 1)];
            Ids.Add(recognitionId);
            Regions.Add(region);
            return Task.FromResult(new WordRecognitionResult(1000, 500,
                sample.Split('\n').Select((line, row) => new OcrLine(line.Select((c, i) => new OcrWord(
                    c.ToString(), new(i * 10, Calls + row * 80, 10, 20))).ToArray())).ToArray()));
        }
    }

    private sealed class RecordingAnalyzer : ILlmPhraseAnalyzer
    {
        public List<string> Texts { get; } = [];
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Texts.Add(request.SegmentText);
            return Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        }
    }

    private sealed class Tokenizer : ITokenizer
    {
        public IReadOnlyList<Token> Tokenize(string? text) => (text ?? "").Select((c, i) => new Token(
            c.ToString(), c.ToString(), c.ToString(), c.ToString(), c.ToString(), c.ToString(),
            new("noun", "", "", ""), "", "", "", i)).ToArray();
    }
    private sealed class Grouping : IOcrWordGroupingService
    {
        public IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines) => [];
    }
    private sealed class Overlay : IOverlayRenderer
    {
        public void Show(WordOverlaySession session, Func<bool>? canRender = null) { }
        public void Hide() { }
        public void Suspend() { }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly Channel<Timer> _timers = Channel.CreateUnbounded<Timer>();
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(callback, state, dueTime);
            _timers.Writer.TryWrite(timer);
            return timer;
        }

        public async Task TickAsync()
        {
            var timer = await _timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Interlocked.Add(ref _timestamp, timer.DueTime.Ticks);
            timer.Fire();
        }

        private sealed class Timer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private bool _disposed;
            public TimeSpan DueTime { get; } = dueTime;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

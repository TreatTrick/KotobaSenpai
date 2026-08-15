using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Core.Tests;

public sealed class PhraseHoverResolverTests
{
    [Fact]
    public void Returns_minus_one_when_cursor_hits_nothing()
    {
        var groups = new[] { Group(0, 2, Rects(0, 0, 10, 10)) };
        Assert.Equal(-1, PhraseHoverResolver.Resolve(groups, 100, 100));
    }

    [Fact]
    public void Prefers_group_with_fewer_distinct_tokens()
    {
        // 短 group（1 token）与包含它的长 group（2 token）重叠，光标同时命中两者。
        var groups = new[]
        {
            Group(0, 2, Rects(0, 0, 20, 20)),
            Group(1, 1, Rects(0, 0, 20, 20)),
        };
        Assert.Equal(1, PhraseHoverResolver.Resolve(groups, 10, 10));
    }

    [Fact]
    public void Breaks_tie_by_provider_order()
    {
        var groups = new[]
        {
            Group(1, 1, Rects(0, 0, 20, 20)),
            Group(0, 1, Rects(0, 0, 20, 20)),
        };
        Assert.Equal(1, PhraseHoverResolver.Resolve(groups, 10, 10));
    }

    [Fact]
    public void Hits_part_on_any_line_of_cross_line_group()
    {
        var groups = new[]
        {
            Group(0, 2, Rects(0, 0, 10, 10), Rects(0, 100, 10, 10)),
        };
        Assert.Equal(0, PhraseHoverResolver.Resolve(groups, 5, 105));
    }

    private static PhraseGroupView Group(int providerOrder, int distinctTokens, params PhrasePartView[] parts)
        => new(Guid.NewGuid(), "label", "type", "meaning", "grammar", providerOrder, distinctTokens, parts);

    private static PhrasePartView Rects(int x, int y, int w, int h)
        => new([new ScreenRect(x, y, w, h)]);
}

public sealed class PhraseSessionTests
{
    [Fact]
    public void Session_carries_phrase_groups_and_warning()
    {
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));
        var group = new PhraseGroupView(Guid.NewGuid(), "label", "type", "m", "g", 0, 1,
            [new PhrasePartView([new ScreenRect(0, 0, 10, 10)])]);
        var session = WordOverlaySession.Start(target, [], [group], "provider timed out");

        Assert.Single(session.PhraseGroups);
        Assert.Equal("provider timed out", session.PhraseWarning);
    }

    [Fact]
    public void Session_defaults_to_empty_phrase_groups()
    {
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));
        var session = WordOverlaySession.Start(target, []);
        Assert.Empty(session.PhraseGroups);
        Assert.Null(session.PhraseWarning);
    }
}

public sealed class PhraseFallbackTests
{
    [Fact]
    public async Task App_service_keeps_local_words_when_phrase_disabled()
    {
        var (service, overlay) = BuildService(analyzer: null, settings: new FakeSettings("false"));
        await service.RecognizeAndShowAsync(Target());

        Assert.Single(overlay.Session!.Words);
        Assert.Empty(overlay.Session.PhraseGroups);
        Assert.Null(overlay.Session.PhraseWarning);
    }

    [Fact]
    public async Task App_service_keeps_local_words_when_provider_unavailable()
    {
        var (service, overlay) = BuildService(
            analyzer: new FakeAnalyzer(PhraseAnalysisOutcome.NoKey),
            settings: new FakeSettings("true"));
        await service.RecognizeAndShowAsync(Target());

        Assert.Single(overlay.Session!.Words);
        Assert.Empty(overlay.Session.PhraseGroups);
        Assert.NotNull(overlay.Session.PhraseWarning);
    }

    [Fact]
    public async Task App_service_maps_valid_phrase_group_to_screen_when_enabled()
    {
        var analyzer = new FakeAnalyzer(PhraseAnalysisOutcome.Success,
            new ParsedPhraseGroup("g1", "grammar", [[SentenceTokenId.Parse("l0:t0")]], "彼は", "他是", "主语助词"));
        var (service, overlay) = BuildService(analyzer, new FakeSettings("true"));
        await service.RecognizeAndShowAsync(Target());

        var group = Assert.Single(overlay.Session!.PhraseGroups);
        Assert.Equal("他是", group.Meaning);
        Assert.NotEqual(Guid.Empty, group.SessionGroupId);
        // 帧 100x50 → 窗口 200x100（2x 缩放），part 框 (0,0,10,20) → 屏幕 (0,0,20,40)。
        var rect = Assert.Single(group.Parts[0].Rects);
        Assert.Equal(new ScreenRect(0, 0, 20, 40), rect);
    }

    private static WindowTarget Target() => new((nint)42, "VN", new ScreenRect(0, 0, 200, 100));

public sealed class PhraseOrchestratorConcurrencyTests
{
    private static OcrLine Line(string text)
        => new(text.Select((c, i) => new OcrWord(c.ToString(), new ScreenRect(i * 10, 0, 10, 20))).ToArray());

    private static readonly IReadOnlyList<OcrLine> FourSegments =
        [Line("ご。"), Line("き。"), Line("ち。"), Line("は。")];

    [Fact]
    public async Task Runs_segments_concurrently_and_preserves_order()
    {
        var analyzer = new TrackingAnalyzer(request => Success(request));
        var orchestrator = new PhraseAnalysisOrchestrator(
            analyzer, new SentenceSegmenter(), new SentenceTokenBuilder(new CharTokenizer()));

        var run = await orchestrator.AnalyzeAsync(FourSegments);

        Assert.Equal(4, run.Groups.Count);
        // 顺序与 segment 顺序一致（Task.WhenAll 保序）。
        Assert.Equal(["s0-0", "s1-1", "s2-2", "s3-3"], run.Groups.Select(g => g.Label).ToArray());
        // 至少一度有多个请求在途，证明并发而非串行。
        Assert.True(analyzer.MaxInFlight > 1, $"expected concurrent calls, max in-flight was {analyzer.MaxInFlight}");
    }

    [Fact]
    public async Task Continues_with_other_segments_when_one_fails()
    {
        var analyzer = new TrackingAnalyzer(request =>
            request.SegmentId == "s0-0"
                ? new PhraseAnalysisResult(PhraseAnalysisOutcome.NoKey, [], null)
                : Success(request));
        var orchestrator = new PhraseAnalysisOrchestrator(
            analyzer, new SentenceSegmenter(), new SentenceTokenBuilder(new CharTokenizer()));

        var run = await orchestrator.AnalyzeAsync(FourSegments);

        Assert.Equal(PhraseAnalysisOutcome.Success, run.Outcome);
        Assert.Equal(3, run.Groups.Count);
        Assert.NotNull(run.Warning);
        Assert.Contains("requires provider configuration", run.Warning);
    }

    [Fact]
    public async Task Returns_cancelled_when_cancelled_before_requests_run()
    {
        var analyzer = new TrackingAnalyzer(request => Success(request));
        var orchestrator = new PhraseAnalysisOrchestrator(
            analyzer, new SentenceSegmenter(), new SentenceTokenBuilder(new CharTokenizer()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var run = await orchestrator.AnalyzeAsync(FourSegments, cts.Token);

        Assert.Equal(PhraseAnalysisOutcome.Cancelled, run.Outcome);
        Assert.Empty(run.Groups);
    }

    private static PhraseAnalysisResult Success(PhraseAnalysisRequest request)
        => new(PhraseAnalysisOutcome.Success,
            [new ParsedPhraseGroup(request.SegmentId, "grammar", [[request.Tokens[0].Id]], request.SegmentId, "意思", "语法")]);

    private sealed class TrackingAnalyzer : ILlmPhraseAnalyzer
    {
        private readonly Func<PhraseAnalysisRequest, PhraseAnalysisResult> _respond;
        private int _inFlight;
        private int _maxInFlight;

        public TrackingAnalyzer(Func<PhraseAnalysisRequest, PhraseAnalysisResult> respond) => _respond = respond;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public async Task<PhraseAnalysisResult> AnalyzeAsync(
            PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int observed;
            while (now > (observed = Volatile.Read(ref _maxInFlight)))
                Interlocked.CompareExchange(ref _maxInFlight, now, observed);

            try
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                return _respond(request);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }
}

    private static (WordOverlayApplicationService Service, FakeOverlay Overlay) BuildService(
        ILlmPhraseAnalyzer? analyzer,
        ISettingsService settings)
    {
        var phraseOrchestrator = analyzer is null
            ? null
            : new PhraseAnalysisOrchestrator(analyzer, new SentenceSegmenter(), new SentenceTokenBuilder(new CharTokenizer()));
        var overlay = new FakeOverlay();
        var service = new WordOverlayApplicationService(
            new FakeRecognizer(),
            new WordGroupingService(new CharTokenizer()),
            overlay,
            phraseOrchestrator: phraseOrchestrator,
            settings: settings);
        return (service, overlay);
    }

    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _enabled;
        public FakeSettings(string? enabled) => _enabled = enabled;
        public string? GetValue(string key) => key == "PhraseGroupsEnabled" ? _enabled : null;
        public void SetValue(string key, string? value) { }
    }

    private sealed class FakeAnalyzer : ILlmPhraseAnalyzer
    {
        private readonly PhraseAnalysisOutcome _outcome;
        private readonly ParsedPhraseGroup[] _groups;
        public FakeAnalyzer(PhraseAnalysisOutcome outcome, params ParsedPhraseGroup[] groups)
            => (_outcome, _groups) = (outcome, groups);

        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken ct = default)
            => Task.FromResult(new PhraseAnalysisResult(_outcome, _groups));
    }

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordRecognitionResult(100, 50,
                [new OcrLine([new OcrWord("彼", new ScreenRect(0, 0, 10, 20))])]));
    }

    private sealed class FakeOverlay : IOverlayRenderer
    {
        public WordOverlaySession? Session { get; private set; }
        public void Show(WordOverlaySession session) => Session = session;
        public void Hide() => Session = null;
    }
}
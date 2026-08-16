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
        // A short group (1 token) overlaps the longer group (2 tokens) that contains it; the cursor hits both.
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

public sealed class WordMeaningValidatorTests
{
    private static readonly IReadOnlyList<LocalSpanSummary> Spans =
    [
        new LocalSpanSummary("でも", "でも", "でも", [SentenceTokenId.Parse("l0:t0"), SentenceTokenId.Parse("l0:t1")]),
        new LocalSpanSummary("食べる", "たべる", "食べる", [SentenceTokenId.Parse("l0:t2")]),
    ];

    [Fact]
    public void Accepts_valid_word_and_carries_merged_surface_and_reading()
    {
        var result = WordMeaningValidator.ValidateAndBuild(
            [Word("でも", "接続助詞", "即使", "强调让步")], Spans);
        var word = Assert.Single(result.ValidWords);
        Assert.Equal("でも", word.Headword);
        Assert.Equal("でも", word.Reading);
        Assert.Equal("接続助詞", word.Pos);
        Assert.Equal(0, result.DroppedCount);
    }

    [Fact]
    public void Drops_word_with_unmatched_headword()
    {
        var result = WordMeaningValidator.ValidateAndBuild(
            [Word("不存在", "名詞", "学校", "名詞")], Spans);
        Assert.Empty(result.ValidWords);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Drops_word_that_repeats_a_headword()
    {
        var result = WordMeaningValidator.ValidateAndBuild(
            [Word("でも", "助詞", "即使", "让步"), Word("でも", "助詞", "即使", "让步")], Spans);
        Assert.Single(result.ValidWords);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Retains_valid_word_alongside_unmatched_one()
    {
        var result = WordMeaningValidator.ValidateAndBuild(
            [Word("不存在", "名詞", "学校", "名詞"), Word("食べる", "動詞", "吃", "他動・五段")], Spans);
        var word = Assert.Single(result.ValidWords);
        Assert.Equal("食べる", word.Headword);
        Assert.Equal(1, result.DroppedCount);
    }

    [Fact]
    public void Caps_at_max_words_per_segment()
    {
        var spans = Enumerable.Range(0, ParsedWordMeaning.MaxWordsPerSegment + 2)
            .Select(i => new LocalSpanSummary($"w{i}", $"w{i}", $"w{i}", [SentenceTokenId.Parse($"l0:t{i}")]))
            .ToArray();
        var words = Enumerable.Range(0, ParsedWordMeaning.MaxWordsPerSegment + 2)
            .Select(i => Word($"w{i}", "動詞", "吃", "他動・五段"))
            .ToArray();
        var result = WordMeaningValidator.ValidateAndBuild(words, spans);
        Assert.Equal(ParsedWordMeaning.MaxWordsPerSegment, result.ValidWords.Count);
        Assert.Equal(2, result.DroppedCount);
    }

    [Fact]
    public void Drops_oversized_pos()
    {
        var result = WordMeaningValidator.ValidateAndBuild(
            [Word("でも", new string('x', ParsedWordMeaning.MaxPosLength + 1), "即使", "让步")], Spans);
        Assert.Empty(result.ValidWords);
    }

    [Fact]
    public void Handles_a_word_that_appears_twice_without_throwing()
    {
        var spans = new[]
        {
            new LocalSpanSummary("は", "ハ", "は", [SentenceTokenId.Parse("l0:t0")]),
            new LocalSpanSummary("は", "ハ", "は", [SentenceTokenId.Parse("l0:t3")]),
        };
        var result = WordMeaningValidator.ValidateAndBuild(
            [Word("は", "助詞", "（主题）", "提示主题")], spans);
        var word = Assert.Single(result.ValidWords);
        Assert.Equal("は", word.Headword);
    }

    private static ParsedWordMeaning Word(string headword, string pos, string meaning, string grammar)
        => new(headword, pos, meaning, grammar);
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

    [Fact]
    public void Session_maps_word_meaning_by_merged_surface_and_reading()
    {
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));
        var word = new GroupedWord(TokenOf("でも", "でも"), new ScreenRect(0, 0, 20, 20));
        var meaning = new WordMeaningView("でも", "でも", "接続助詞", "即使", "让步");
        var session = WordOverlaySession.Start(target, [word], wordMeanings: [meaning]);

        Assert.Equal(meaning, session.TryGetMeaning(word));
        Assert.Null(session.TryGetMeaning(new GroupedWord(TokenOf("食べる", "たべる"), new ScreenRect(0, 0, 20, 20))));
    }

    [Fact]
    public void Group_member_meanings_filters_words_without_meaning()
    {
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));
        var w0 = new GroupedWord(TokenOf("あ", "あ"), new ScreenRect(0, 0, 10, 20));
        var w1 = new GroupedWord(TokenOf("食べる", "たべる"), new ScreenRect(10, 0, 10, 20));
        var w2 = new GroupedWord(TokenOf("う", "う"), new ScreenRect(20, 0, 10, 20));
        var meaning = new WordMeaningView("食べる", "たべる", "他動・五段", "吃", "动词");
        var session = WordOverlaySession.Start(target, [w0, w1, w2], wordMeanings: [meaning]);
        var group = new PhraseGroupView(Guid.NewGuid(), "label", "type", "m", "g", 0, 2,
            [new PhrasePartView([new ScreenRect(0, 0, 20, 20)])]); // covers w0 + w1

        var memberMeanings = session.GetCoveredWordIndices(group)
            .Select(i => session.Words[i])
            .Select(session.TryGetMeaning)
            .Where(m => m is not null)
            .Cast<WordMeaningView>()
            .ToArray();
        Assert.Equal([meaning], memberMeanings);
    }

    [Fact]
    public void Session_maps_repeated_word_to_single_meaning_without_throwing()
    {
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));
        var meaning = new WordMeaningView("は", "ハ", "助詞", "（主题）", "提示主题");
        var session = WordOverlaySession.Start(target,
            [
                new GroupedWord(TokenOf("は", "ハ"), new ScreenRect(0, 0, 10, 20)),
                new GroupedWord(TokenOf("は", "ハ"), new ScreenRect(20, 0, 10, 20)),
            ],
            wordMeanings: [meaning]);

        Assert.Equal(meaning, session.TryGetMeaning(session.Words[0]));
        Assert.Equal(meaning, session.TryGetMeaning(session.Words[1]));
    }

    [Fact]
    public void Group_hover_covers_only_member_word_underlines()
    {
        var target = new WindowTarget((nint)1, "VN", new ScreenRect(0, 0, 100, 100));
        // Words at x=0..10 (w0), x=10..20 (w1), x=20..30 (w2); a group covering only w1's rect.
        var w0 = new GroupedWord(TokenOf("あ", "あ"), new ScreenRect(0, 0, 10, 20));
        var w1 = new GroupedWord(TokenOf("でも", "でも"), new ScreenRect(10, 0, 10, 20));
        var w2 = new GroupedWord(TokenOf("う", "う"), new ScreenRect(20, 0, 10, 20));
        var session = WordOverlaySession.Start(target, [w0, w1, w2]);
        var group = new PhraseGroupView(Guid.NewGuid(), "label", "type", "m", "g", 0, 1,
            [new PhrasePartView([new ScreenRect(10, 0, 10, 20)])]);

        var covered = session.GetCoveredWordIndices(group);
        Assert.Equal([1], covered);
    }

    private static Token TokenOf(string surface, string reading)
        => new(surface, reading, surface, reading, surface, reading,
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);
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
        // Frame 100x50 → window 200x100 (2x scale), part box (0,0,10,20) → screen (0,0,20,40).
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
        // Order matches segment order (Task.WhenAll preserves order).
        Assert.Equal(["s0-0", "s1-1", "s2-2", "s3-3"], run.Groups.Select(g => g.Label).ToArray());
        // At least once several requests were in flight, proving concurrency rather than serialization.
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
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
    [Theory]
    [InlineData(false, "「また会おう」", "彼女は微笑んだ。")]
    [InlineData(false, "また会おう」", "それは助かる。")]
    [InlineData(true, "「また会おう」", "彼女は微笑んだ。")]
    [InlineData(true, "最初の文。", "次の文。")]
    public async Task Completed_block_keeps_its_annotations_when_narration_or_unquoted_text_is_appended(
        bool sameLine, string completed, string added)
    {
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        IReadOnlyList<OcrLine> next = sameLine ? [Line(completed + added)] : [Line(completed), Line(added, 30)];
        using var service = CreateService(new SequencedRecognizer([Line(completed)], next), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession!;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            var preview = overlay.LastSession!;
            Assert.Equal(previous.PhraseGroups.Select(group => group.SessionGroupId),
                preview.PhraseGroups.Select(group => group.SessionGroupId));
            Assert.Equal(previous.WordMeanings.Select(word => word.Headword), preview.WordMeanings.Select(word => word.Headword));
            Assert.Equal(previous.GetSentenceTranslation("s0-0")!.Translation,
                preview.GetSentenceTranslation("s0-0")!.Translation);
            Assert.All(previous.Lines, line => Assert.Contains(line, preview.Lines));
            Assert.Contains("s0-0", preview.UnderlineSegmentIds!);
            Assert.DoesNotContain("s1-1", preview.UnderlineSegmentIds!);
            Assert.Equal([completed, added], analyzer.Requests.Select(request => request.SegmentText));
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
        Assert.Contains("s1-1", overlay.LastSession!.UnderlineSegmentIds!);
    }

    [Fact]
    public async Task Completed_block_survives_when_another_block_changes_and_its_opening_quote_is_lost()
    {
        const string first = "「最初の発言」";
        const string second = "「次の発言」";
        const string changed = "「変更された発言」";
        const string narration = "彼女は微笑んだ。";
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line(first), Line(second, 30)],
            [Line(changed), Line(second[1..], 30), Line(narration, 60)]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var retained = overlay.LastSession!.PhraseGroups.Single(group => group.SegmentId == "s1-1").SessionGroupId;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            Assert.Equal(retained, Assert.Single(overlay.LastSession!.PhraseGroups).SessionGroupId);
            Assert.Equal(new[] { "s1-1" }, overlay.LastSession.UnderlineSegmentIds!);
            Assert.Equal([first, second, changed, narration], analyzer.Requests.Select(request => request.SegmentText));
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
    }

    [Fact]
    public async Task Completed_block_planning_keeps_an_initial_cross_line_quotation_with_its_reporting_clause()
    {
        var analyzer = new RecordingAnalyzer(new(TaskCreationOptions.RunContinuationsAsynchronously));
        using var service = CreateService(new SequencedRecognizer([Line("彼は「明日行く」"), Line("と言った。", 30)]),
            new RecordingOverlay(), analyzer);

        await service.RecognizeAndShowAsync(Target());

        Assert.Equal("彼は「明日行く」と言った。", Assert.Single(analyzer.Requests).SegmentText);
    }

    [Fact]
    public async Task The_recorded_four_sentence_sequence_preserves_every_completed_annotation()
    {
        const string first = "悪いけど、優しくしてあげる気はないから。";
        const string second = "時間も惜しいし、手早くいくわよ」";
        const string third = "「それは助かる。やっぱり時間は大切だ」";
        const string fourth = "青子の直球は、またも効果なくスルーされた。";
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line("「" + first)],
            [Line(first), Line(second, 40)],
            [Line(first), Line(second, 40), Line(third, 80)],
            [Line(first), Line(second + "、", 40), Line(third, 80), Line("いやみ", 180), Line(fourth, 200)]),
            overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        await service.RecognizeChangedAndShowAsync(Target());
        await service.RecognizeChangedAndShowAsync(Target());
        var previous = overlay.LastSession!;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            var preview = overlay.LastSession!;
            Assert.Equal(previous.PhraseGroups.Select(group => group.SessionGroupId),
                preview.PhraseGroups.Select(group => group.SessionGroupId));
            Assert.Equal(previous.WordMeanings.Select(word => word.Headword), preview.WordMeanings.Select(word => word.Headword));
            Assert.All(previous.Lines, line => Assert.Contains(line, preview.Lines));
            Assert.Equal(new[] { "s0-0", "s1-1", "s2-2" }, preview.UnderlineSegmentIds!.Order());
            Assert.Equal(["「" + first, second, third, "いやみ" + fourth],
                analyzer.Requests.Select(request => request.SegmentText));
            Assert.Equal(1, overlay.HideCount);
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
        Assert.Contains("s3-4", overlay.LastSession!.UnderlineSegmentIds!);
    }

    [Fact]
    public async Task The_recorded_unpunctuated_dialogue_stays_cached_when_the_next_speaker_appears()
    {
        const string first = "悪いけど、優しくしてあげる気はないから。";
        const string second = "時間も惜しいし、手早くいくわよ」";
        const string third = "「それは助かる。やっぱり時間は大切だ」";
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line(first), Line(second, 40)],
            [Line(first), Line(second, 40), Line(third, 80)]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession!;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            Assert.Equal(previous.PhraseGroups.Select(group => group.SessionGroupId),
                overlay.LastSession!.PhraseGroups.Select(group => group.SessionGroupId));
            Assert.Contains("s1-1", overlay.LastSession.UnderlineSegmentIds!);
            Assert.Equal([first, second, third], analyzer.Requests.Select(request => request.SegmentText));
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
    }

    [Fact]
    public async Task The_recorded_trailing_comma_after_a_dialogue_quote_preserves_cached_annotations()
    {
        const string previousText = "時間も惜しいし、手早くいくわよ」";
        const string currentText = "時間も惜しいし、手早くいくわよ」、";
        const string newText = "青子の直球は、またも効果なくスルーされた。";
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line(previousText)],
            [Line(currentText), Line(newText, 100)]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession!;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            Assert.Equal(previous.PhraseGroups.Select(group => group.SessionGroupId),
                overlay.LastSession!.PhraseGroups.Select(group => group.SessionGroupId));
            Assert.Contains("s0-0", overlay.LastSession.UnderlineSegmentIds!);
            Assert.Equal([previousText, newText], analyzer.Requests.Select(request => request.SegmentText));
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
    }

    [Fact]
    public async Task The_recorded_third_dialogue_reuses_both_older_sentences_despite_an_added_opening_quote()
    {
        const string first = "いや、いいんだ、ごく一般的なんだろう。";
        const string second = "静希君で頼む。君は蒼崎でいいんだよな?」";
        const string third = "「ええ、よろしく」";
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line(first), Line(second, 40)],
            [Line("「" + first), Line(second, 40), Line(third, 80)]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession!;
        analyzer.DelayNewRequests = true;

        var pending = service.RecognizeChangedAndShowAsync(Target());
        try
        {
            var preview = overlay.LastSession!;
            Assert.Contains("s0-0", preview.UnderlineSegmentIds!);
            Assert.Contains("s1-1", preview.UnderlineSegmentIds!);
            Assert.DoesNotContain("s2-2", preview.UnderlineSegmentIds!);
            Assert.Equal(previous.PhraseGroups.Select(group => group.SessionGroupId),
                preview.PhraseGroups.Select(group => group.SessionGroupId));
            Assert.Equal(previous.WordMeanings.Select(word => word.Headword), preview.WordMeanings.Select(word => word.Headword));
            Assert.Equal([first, second, third], analyzer.Requests.Select(request => request.SegmentText));
            Assert.Equal(1, overlay.HideCount);
        }
        finally
        {
            release.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await pending;
        }
        Assert.Contains("s2-2", overlay.LastSession!.UnderlineSegmentIds!);
    }

    [Fact]
    public async Task A_waiting_snapshot_shows_newly_cached_sentences_before_its_own_model_request_finishes()
    {
        var analyzer = new SentenceAnalyzer();
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line("旧句。")],
            [Line("旧句。"), Line("追加。", 60)], [Line("旧句。"), Line("追加。", 60), Line("最新。", 120)]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        var first = await service.BeginChangedAndShowAsync(Target());
        var second = await service.BeginChangedAndShowAsync(Target());
        analyzer.First.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        await analyzer.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Contains("s1-1", overlay.LastSession!.UnderlineSegmentIds!);
            Assert.DoesNotContain("s2-2", overlay.LastSession.UnderlineSegmentIds!);
        }
        finally
        {
            analyzer.Second.TrySetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
            await Task.WhenAll(first!.Completion, second!.Completion);
        }
    }

    private sealed class SentenceAnalyzer : ILlmPhraseAnalyzer
    {
        public TaskCompletionSource<PhraseAnalysisResult> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PhraseAnalysisResult> Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PhraseAnalysisResult> AnalyzeAsync(PhraseAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            if (request.SegmentText == "追加。") return First.Task;
            if (request.SegmentText == "最新。") { SecondStarted.TrySetResult(); return Second.Task; }
            return Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        }
    }

    [Fact]
    public async Task Automatic_observation_returns_while_analysis_is_pending_and_keeps_only_the_latest_waiting_snapshot()
    {
        var release = new TaskCompletionSource<PhraseAnalysisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analyzer = new RecordingAnalyzer(release);
        var overlay = new RecordingOverlay();
        using var service = CreateService(new SequencedRecognizer([Line("旧句。")],
            [Line("旧句。"), Line("最初。", 60)], [Line("旧句。"), Line("途中。", 60)],
            [Line("旧句。"), Line("最後。", 60)]), overlay, analyzer);
        await service.RecognizeAndShowAsync(Target());
        analyzer.DelayNewRequests = true;

        var first = await service.BeginChangedAndShowAsync(Target()).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(first!.Completion.IsCompleted);
        var middle = await service.BeginChangedAndShowAsync(Target()).WaitAsync(TimeSpan.FromSeconds(1));
        var last = await service.BeginChangedAndShowAsync(Target()).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(await middle!.Completion);
        Assert.Contains(overlay.LastSession!.Words, word => word.Surface == "最");
        Assert.Contains("s0-0", overlay.LastSession.UnderlineSegmentIds!);
        release.SetResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success, []));
        Assert.False(await first.Completion);
        Assert.True(await last!.Completion);
        Assert.DoesNotContain(analyzer.Requests, request => request.SegmentText == "途中。");
        Assert.Equal(["旧句。", "最初。", "最後。"], analyzer.Requests.Select(request => request.SegmentText));
    }

    [Fact]
    public async Task Translation_setting_keeps_the_overlay_and_reanalyzes_unchanged_text_on_the_next_scan()
    {
        var preferences = new SentenceTranslationPreferences(new TranslationSettings());
        var overlay = new RecordingOverlay();
        var recognizer = new SequencedRecognizer([Line("同じ。")], [Line("同じ。")]);
        var analyzer = new CountingAnalyzer();
        using var service = CreateService(recognizer, overlay, analyzer, translation: preferences);
        await service.RecognizeAndShowAsync(Target());
        var previous = overlay.LastSession;
        preferences.Enabled = false;
        Assert.Same(previous, overlay.LastSession);
        Assert.Equal(1, analyzer.CallCount);
        await service.RecognizeChangedAndShowAsync(Target());
        Assert.Equal(2, analyzer.CallCount);
    }

    private sealed class TranslationSettings : KotobaSenpai.TestSupport.SettingsStub
    {
        public override string? GetValue(string key) => null;
        public override void SetValue(string key, string? value) { }
    }

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
        IExplanationLanguagePreferences? language = null,
        SentenceTranslationPreferences? translation = null)
        => new(
            recognizer,
            new WordGroupingService(new CharTokenizer(), new SentenceSegmenter(), new IdentitySpanResolver()),
            overlay,
            phraseOrchestrator: new PhraseAnalysisOrchestrator(analyzer, new SentenceSegmenter(),
                new SentenceTokenBuilder(new CharTokenizer(), new IdentitySpanResolver()), language, sentenceTranslation: translation),
            explanationLanguage: language, sentenceTranslation: translation);

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
            var content = request.Tokens.First(reference => !reference.Token.Surface.All(c => "「」『』".Contains(c)));
            return DelayNewRequests
                ? release.Task
                : Task.FromResult(new PhraseAnalysisResult(PhraseAnalysisOutcome.Success,
                    [new ParsedPhraseGroup("first", "grammar", [[content.Id]], "label", "meaning", "grammar")])
                {
                    Words = [new ParsedWordMeaning(content.Token.Surface, "noun", "meaning", "grammar")],
                    Translation = "Translation",
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

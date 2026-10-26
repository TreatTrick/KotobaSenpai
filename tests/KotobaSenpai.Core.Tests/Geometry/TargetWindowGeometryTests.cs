using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Tests.Geometry;

public sealed class TargetWindowGeometryTests
{
    [Fact]
    public void Normalized_rect_reprojects_from_original_bounds_without_accumulation()
    {
        var original = new ScreenRect(100, 50, 1000, 500);
        var rect = new ScreenRect(350, 175, 200, 100);
        var normalized = NormalizedRect.FromScreen(rect, original);

        Assert.Equal(new ScreenRect(700, 350, 400, 200), normalized.ToScreen(new ScreenRect(200, 100, 2000, 1000)));
        Assert.Equal(new ScreenRect(350, 175, 200, 100), normalized.ToScreen(original));
    }

    [Fact]
    public void Named_overlay_geometry_reprojects_each_line_rect()
    {
        var original = new ScreenRect(100, 50, 1000, 500);
        var geometry = new NormalizedWordGeometry(
        [
            NormalizedRect.FromScreen(new ScreenRect(350, 175, 200, 100), original),
            NormalizedRect.FromScreen(new ScreenRect(200, 300, 150, 80), original),
        ]);

        Assert.Equal(
            [
                new ScreenRect(700, 350, 400, 200),
                new ScreenRect(400, 600, 300, 160),
            ],
            geometry.ToScreen(new ScreenRect(200, 100, 2000, 1000)));
    }

    [Fact]
    public void Session_reprojects_words_from_stable_geometry()
    {
        var original = new WindowTarget((nint)42, "target", new ScreenRect(100, 50, 1000, 500));
        var session = WordOverlaySession.Start(
            original,
            [new GroupedWord(Token("日本"), new ScreenRect(350, 175, 200, 100))]);

        var moved = session.Reproject(original.WithBounds(new ScreenRect(200, 100, 2000, 1000)));

        Assert.Equal(new ScreenRect(700, 350, 400, 200), moved.Words[0].Bounds);
        Assert.Equal(700, moved.Lines[0].X);
        Assert.Equal(548, moved.Lines[0].Y);
        Assert.Equal(400, moved.Lines[0].Width);
        Assert.Equal(2, moved.Lines[0].Thickness);
    }

    [Fact]
    public void Session_reprojects_source_token_line_geometry_with_the_word()
    {
        var original = new WindowTarget((nint)42, "target", new ScreenRect(100, 50, 1000, 500));
        var first = Token("困っ", 0);
        var second = Token("ちゃう", 2);
        var span = new LookupSpan([first, second], "困っちゃう", []);
        var word = new GroupedWord(
            span,
            [new ScreenRect(300, 100, 100, 20), new ScreenRect(200, 200, 150, 20)],
            [
                [new SourceTokenLineGeometry(0, 0, 2, new ScreenRect(300, 100, 100, 20))],
                [new SourceTokenLineGeometry(1, 0, 3, new ScreenRect(200, 200, 150, 20))],
            ]);

        var moved = WordOverlaySession.Start(original, [word])
            .Reproject(original.WithBounds(new ScreenRect(200, 100, 2000, 1000)));

        Assert.Equal([new ScreenRect(600, 200, 200, 40)], moved.Words[0].GetSourceTokenBounds(0));
        Assert.Equal([new ScreenRect(400, 400, 300, 40)], moved.Words[0].GetSourceTokenBounds(1));
    }

    [Fact]
    public void Session_reprojects_phrase_parts_with_the_same_baseline()
    {
        var original = new WindowTarget((nint)42, "target", new ScreenRect(100, 50, 1000, 500));
        var group = new PhraseGroupView(
            Guid.NewGuid(), "label", "type", "meaning", "grammar", 0, 1,
            [new PhrasePartView([new ScreenRect(350, 175, 200, 100)])]);
        var session = WordOverlaySession.Start(original, [], [group]);

        var moved = session.Reproject(original.WithBounds(new ScreenRect(200, 100, 2000, 1000)));

        Assert.Equal(new ScreenRect(700, 350, 400, 200), moved.PhraseGroups[0].Parts[0].Rects[0]);
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    public async Task Application_service_allows_occlusion_but_rejects_unavailable_targets(bool visible, bool minimized, bool valid, bool expectedCapture)
    {
        var recognizer = new CountingRecognizer();
        var target = new WindowTarget((nint)42, "target", new ScreenRect(0, 0, 100, 100));
        var tracker = new FakeTracker(new TargetWindowSnapshot(
            target.Handle, target.Title, target.Bounds, 1, visible, minimized, false, true, valid));
        var service = new WordOverlayApplicationService(
            recognizer,
            new EmptyGroupingService(),
            new NullOverlay(),
            KotobaSenpai.TestSupport.PhraseAnalysisStub.Create(),
            tracker: tracker);

        if (expectedCapture)
        {
            await service.RecognizeAndShowAsync(target);
            Assert.Equal(1, recognizer.CallCount);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => service.RecognizeAndShowAsync(target));
            Assert.Equal(ErrorCodes.TargetWindowUnavailable, ex.ErrorCode);
            Assert.Equal(0, recognizer.CallCount);
        }
    }

    private static Token Token(string surface, int start = 0)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", start);

    private sealed class CountingRecognizer : IWindowWordRecognizer
    {
        public int CallCount { get; private set; }

        public Task<WordRecognitionResult> RecognizeAsync(
            Guid recognitionId,
            WindowTarget target,
            CancellationToken cancellationToken = default,
            ScreenRect? region = null)
        {
            CallCount++;
            return Task.FromResult(new WordRecognitionResult(1, 1, []));
        }
    }

    private sealed class EmptyGroupingService : IOcrWordGroupingService
    {
        public IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines) => [];
    }

    private sealed class NullOverlay : IOverlayRenderer
    {
        public void Show(WordOverlaySession session, Func<bool>? canRender = null) { }

        public void Hide() { }
    }

    private sealed class FakeTracker(TargetWindowSnapshot snapshot) : ITargetWindowTracker
    {
        public event EventHandler<TargetWindowSnapshot>? Changed;

        public TargetWindowSnapshot? Current { get; private set; } = snapshot;

        public TargetWindowSnapshot Attach(WindowTarget target) => Current!;

        public TargetWindowSnapshot Refresh() => Current!;

        public void Detach() => Current = null;

        public void Dispose() { }

        public void Raise() => Changed?.Invoke(this, Current!);
    }
}

using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

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

    [Fact]
    public async Task Application_service_rejects_occluded_target_before_ocr()
    {
        var recognizer = new CountingRecognizer();
        var target = new WindowTarget((nint)42, "target", new ScreenRect(0, 0, 100, 100));
        var tracker = new FakeTracker(new TargetWindowSnapshot(
            target.Handle, target.Title, target.Bounds, 1, true, false, false, true));
        var service = new WordOverlayApplicationService(
            recognizer,
            new EmptyGroupingService(),
            new NullOverlay(),
            tracker: tracker);

        var ex = await Assert.ThrowsAsync<BusinessRuleValidationException>(() => service.RecognizeAndShowAsync(target));

        Assert.Equal(ErrorCodes.TargetWindowUnavailable, ex.ErrorCode);
        Assert.Equal(0, recognizer.CallCount);
    }

    private static Token Token(string surface)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);

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
        public void Show(WordOverlaySession session) { }

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

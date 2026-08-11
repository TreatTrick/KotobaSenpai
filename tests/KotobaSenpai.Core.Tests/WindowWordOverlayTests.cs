using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

public sealed class WindowWordOverlayTests
{
    [Fact]
    public void Captured_frame_rejects_short_pixel_buffer()
    {
        var ex = Assert.Throws<InvalidFrameException>(() => new CapturedFrame(2, 2, new byte[3]));
        Assert.Equal(ErrorCodes.FramePixelDataTooShort, ex.ErrorCode);
    }

    [Fact]
    public void Screen_rect_rejects_non_positive_dimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenRect(0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenRect(0, 0, 10, 0));
    }

    [Fact]
    public void Coordinate_mapper_scales_and_clips_word_to_window()
    {
        var word = new OcrWord("日本語", new ScreenRect(90, 40, 30, 20));
        var mapped = CoordinateMapper.ToScreen(word, 120, 80, new ScreenRect(100, 200, 600, 400));

        Assert.Equal(new ScreenRect(550, 400, 150, 100), mapped);
    }

    [Fact]
    public void Session_creates_one_line_per_grouped_word()
    {
        var target = new WindowTarget((nint)42, "VN", new ScreenRect(0, 0, 800, 600));
        var session = WordOverlaySession.Start(target,
        [
            new GroupedWord(Token("彼"), new ScreenRect(10, 20, 30, 40)),
            new GroupedWord(Token("が"), new ScreenRect(45, 20, 15, 40))
        ]);

        Assert.Equal(2, session.Lines.Count);
        Assert.Equal(10, session.Lines[0].X);
        Assert.Equal(58, session.Lines[0].Y);
        Assert.Equal(30, session.Lines[0].Width);
    }

    [Fact]
    public void Session_handles_empty_word_list_without_throwing()
    {
        var target = new WindowTarget((nint)42, "VN", new ScreenRect(0, 0, 800, 600));
        var session = WordOverlaySession.Start(target, Array.Empty<GroupedWord>());

        Assert.Empty(session.Words);
        Assert.Empty(session.Lines);
    }

    [Fact]
    public async Task Application_service_groups_and_maps_words_to_overlay()
    {
        var target = new WindowTarget((nint)42, "VN", new ScreenRect(0, 0, 200, 100));
        var overlay = new FakeOverlay();
        var service = new WordOverlayApplicationService(
            new FakeRecognizer(),
            new WordGroupingService(new StubTokenizer(new TokenSpec("日", 0))),
            overlay);

        var result = await service.RecognizeAndShowAsync(target);

        Assert.Single(result.Lines);
        Assert.Single(result.Lines[0].Words);
        Assert.NotNull(overlay.Session);
        Assert.Equal("日", overlay.Session!.Words[0].Token.Surface);
        Assert.Equal(new ScreenRect(20, 10, 20, 20), overlay.Session.Words[0].Bounds);
    }

    [Fact]
    public async Task Application_service_preserves_precomputed_lookup_while_mapping_bounds()
    {
        var target = new WindowTarget((nint)42, "VN", new ScreenRect(0, 0, 200, 100));
        var overlay = new FakeOverlay();
        var entry = new DictionaryEntry("日", "にち", []);
        var service = new WordOverlayApplicationService(
            new FakeRecognizer(),
            new WordGroupingService(
                new StubTokenizer(new TokenSpec("日", 0)),
                new StubSpanResolver(new LookupSpan([Token("日")], "日", [entry]))),
            overlay);

        await service.RecognizeAndShowAsync(target);

        var word = Assert.Single(overlay.Session!.Words);
        Assert.True(word.HasResolvedLookup);
        Assert.Same(entry, Assert.Single(word.Entries));
        Assert.Equal("日", word.LookupKey);
        Assert.Equal(new ScreenRect(20, 10, 20, 20), word.Bounds);
    }

    private static Token Token(string surface)
        => new(surface, surface, surface, surface, surface, surface,
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordRecognitionResult(100, 50,
                [new OcrLine([new OcrWord("日", new ScreenRect(10, 5, 10, 10))])]));
    }

    private sealed class FakeOverlay : IOverlayRenderer
    {
        public WordOverlaySession? Session { get; private set; }
        public void Show(WordOverlaySession session) => Session = session;
        public void Hide() => Session = null;
    }
}

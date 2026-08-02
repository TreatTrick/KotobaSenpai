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
    public void Session_creates_one_line_per_word()
    {
        var target = new WindowTarget((nint)42, "VN", new ScreenRect(0, 0, 800, 600));
        var session = WordOverlaySession.Start(target,
        [
            new ScreenWord("彼", new ScreenRect(10, 20, 30, 40)),
            new ScreenWord("が", new ScreenRect(45, 20, 15, 40))
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
        var session = WordOverlaySession.Start(target, Array.Empty<ScreenWord>());

        Assert.Empty(session.Words);
        Assert.Empty(session.Lines);
    }

    [Fact]
    public async Task Application_service_replaces_overlay_with_recognized_words()
    {
        var target = new WindowTarget((nint)42, "VN", new ScreenRect(0, 0, 200, 100));
        var overlay = new FakeOverlay();
        var service = new WordOverlayApplicationService(new FakeRecognizer(), overlay);

        var result = await service.RecognizeAndShowAsync(target);

        Assert.Single(result.Words);
        Assert.NotNull(overlay.Session);
        Assert.Equal("日", overlay.Session!.Words[0].Text);
        Assert.Equal(new ScreenRect(20, 10, 20, 20), overlay.Session.Words[0].Bounds);
    }

    private sealed class FakeRecognizer : IWindowWordRecognizer
    {
        public Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WordRecognitionResult(100, 50,
                [new OcrWord("日", new ScreenRect(10, 5, 10, 10))]));
    }

    private sealed class FakeOverlay : IOverlayRenderer
    {
        public WordOverlaySession? Session { get; private set; }
        public void Show(WordOverlaySession session) => Session = session;
        public void Hide() => Session = null;
    }
}

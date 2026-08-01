using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 应用服务：编排“捕获 -> OCR -> 坐标映射 -> 覆盖层”用例。
/// 仅依赖 Core 端口，平台实现通过依赖注入注入。
/// </summary>
public sealed class WordOverlayApplicationService
{
    private readonly IWindowWordRecognizer _recognizer;
    private readonly IOverlayRenderer _overlay;

    public WordOverlayApplicationService(IWindowWordRecognizer recognizer, IOverlayRenderer overlay)
    {
        _recognizer = recognizer;
        _overlay = overlay;
    }

    public async Task<WordRecognitionResult> RecognizeAndShowAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        var result = await _recognizer.RecognizeAsync(target, cancellationToken).ConfigureAwait(false);
        var screenWords = result.Words
            .Select(word => new ScreenWord(
                word.Text,
                CoordinateMapper.ToScreen(word, result.FrameWidth, result.FrameHeight, target.Bounds)))
            .ToArray();

        _overlay.Show(WordOverlaySession.Start(target, screenWords));
        return result;
    }

    public void Hide() => _overlay.Hide();
}

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
    private readonly IOcrWordGroupingService _grouping;
    private readonly IOverlayRenderer _overlay;
    private readonly IDiagnosticReporter? _diagnostics;

    public WordOverlayApplicationService(
        IWindowWordRecognizer recognizer,
        IOcrWordGroupingService grouping,
        IOverlayRenderer overlay,
        IDiagnosticReporter? diagnostics = null)
    {
        _recognizer = recognizer;
        _grouping = grouping;
        _overlay = overlay;
        _diagnostics = diagnostics;
    }

    public async Task<WordRecognitionResult> RecognizeAndShowAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        var result = await _recognizer.RecognizeAsync(target, cancellationToken).ConfigureAwait(false);
        // 先在帧坐标系内经分词器把字符重组成词（纯逻辑、坐标不变），再把每个词的并集框映射到屏幕。
        var screenWords = _grouping.Group(result.Lines)
            .Select(word => word.WithBounds(
                CoordinateMapper.ToScreen(word.Bounds, result.FrameWidth, result.FrameHeight, target.Bounds)))
            .ToArray();

        _diagnostics?.RecordTokens(target, screenWords);
        _overlay.Show(WordOverlaySession.Start(target, screenWords));
        return result;
    }

    public void Hide() => _overlay.Hide();
}

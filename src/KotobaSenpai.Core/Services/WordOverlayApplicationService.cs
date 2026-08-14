using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

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
    private readonly PhraseAnalysisOrchestrator? _phraseOrchestrator;
    private readonly ISettingsService? _settings;

    public WordOverlayApplicationService(
        IWindowWordRecognizer recognizer,
        IOcrWordGroupingService grouping,
        IOverlayRenderer overlay,
        IDiagnosticReporter? diagnostics = null,
        PhraseAnalysisOrchestrator? phraseOrchestrator = null,
        ISettingsService? settings = null)
    {
        _recognizer = recognizer;
        _grouping = grouping;
        _overlay = overlay;
        _diagnostics = diagnostics;
        _phraseOrchestrator = phraseOrchestrator;
        _settings = settings;
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

        // 本地识别完成后，按设置开关做 phrase 分析；失败只产生警告，本地词/span 保持可用。
        PhraseAnalysisRun? phraseRun = null;
        if (_phraseOrchestrator is not null && IsPhraseEnabled())
        {
            phraseRun = await _phraseOrchestrator
                .AnalyzeAsync(result.Lines, cancellationToken)
                .ConfigureAwait(false);
        }

        var phraseGroups = phraseRun?.Groups
            .Select(group => ToScreen(group, result.FrameWidth, result.FrameHeight, target.Bounds))
            .ToArray() ?? [];

        if (phraseRun is not null)
            _diagnostics?.RecordPhraseAnalysis(phraseRun.Outcome, phraseRun.Groups, phraseRun.Warning);
        _diagnostics?.RecordTokens(target, screenWords);
        _overlay.Show(WordOverlaySession.Start(target, screenWords, phraseGroups, phraseRun?.Warning));
        return result;
    }

    public void Hide() => _overlay.Hide();

    private bool IsPhraseEnabled()
        => string.Equals(_settings?.GetValue("PhraseGroupsEnabled"), "true", StringComparison.OrdinalIgnoreCase);

    private static PhraseGroupView ToScreen(PhraseGroupView group, int frameWidth, int frameHeight, ScreenRect windowBounds)
        => new(
            group.SessionGroupId,
            group.Label,
            group.Type,
            group.MeaningZh,
            group.GrammarZh,
            group.ProviderOrder,
            group.DistinctTokenCount,
            group.Parts
                .Select(part => new PhrasePartView(
                    part.Rects.Select(rect => CoordinateMapper.ToScreen(rect, frameWidth, frameHeight, windowBounds)).ToArray()))
                .ToArray());
}

using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// Application service orchestrating the "capture -> OCR -> coordinate mapping -> overlay" use case.
/// Depends only on Core ports; platform implementations are injected via dependency injection.
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
        var regionPixels = ReadRegionPixels(target);
        var result = await _recognizer.RecognizeAsync(target, cancellationToken, regionPixels).ConfigureAwait(false);
        // First regroup characters into words in the frame coordinate system via the tokenizer (pure logic, coordinates unchanged), then map each word's union box to screen.
        var screenWords = _grouping.Group(result.Lines)
            .Select(word => word.WithRects(word.Rects
                .Select(rect => CoordinateMapper.ToScreen(rect, result.FrameWidth, result.FrameHeight, target.Bounds))
                .ToArray()))
            .ToArray();

        // After local recognition, run phrase analysis per the settings toggle; failure only produces a warning, and local words/spans remain usable.
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
        _overlay.Show(WordOverlaySession.Start(target, screenWords, phraseGroups, phraseRun?.Warning, phraseRun?.Words));
        return result;
    }

    public void Hide() => _overlay.Hide();

    /// <summary>Reads the saved recognition region and converts it to a window/frame pixel rect; null when unset or invalid (=> full window).</summary>
    private ScreenRect? ReadRegionPixels(WindowTarget target)
    {
        if (_settings is null)
            return null;
        var raw = _settings.GetValue(RecognitionRegion.SettingsKey);
        return RecognitionRegion.TryParse(raw, out var region)
            ? region.ToPixelRect(target.Bounds.Width, target.Bounds.Height)
            : null;
    }

    private bool IsPhraseEnabled()
        => string.Equals(_settings?.GetValue("PhraseGroupsEnabled"), "true", StringComparison.OrdinalIgnoreCase);

    private static PhraseGroupView ToScreen(PhraseGroupView group, int frameWidth, int frameHeight, ScreenRect windowBounds)
        => new(
            group.SessionGroupId,
            group.Label,
            group.Type,
            group.Meaning,
            group.Grammar,
            group.ProviderOrder,
            group.DistinctTokenCount,
            group.Parts
                .Select(part => new PhrasePartView(
                    part.Rects.Select(rect => CoordinateMapper.ToScreen(rect, frameWidth, frameHeight, windowBounds)).ToArray()))
                .ToArray());
}

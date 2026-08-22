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
    private int _recognitionGeneration;

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
        var recognitionId = Guid.NewGuid();
        var generation = Interlocked.Increment(ref _recognitionGeneration);
        var regionPixels = ReadRegionPixels(target);
        var result = await _recognizer.RecognizeAsync(recognitionId, target, cancellationToken, regionPixels).ConfigureAwait(false);
        // First regroup characters into words in the frame coordinate system via the tokenizer (pure logic, coordinates unchanged), then map each word's union box to screen.
        var screenWords = _grouping.Group(result.Lines)
            .Select(word => word.WithRects(word.Rects
                .Select(rect => CoordinateMapper.ToScreen(rect, result.FrameWidth, result.FrameHeight, target.Bounds))
                .ToArray()))
            .ToArray();

        _diagnostics?.RecordTokens(recognitionId, target, screenWords);

        // Publish local furigana before the optional provider call. The empty set intentionally suppresses underlines
        // only while phrase analysis is enabled; the disabled path below keeps the legacy all-local overlay.
        var phraseEnabled = _phraseOrchestrator is not null && IsPhraseEnabled();
        if (phraseEnabled)
        {
            if (!IsCurrent(generation))
                return result;
            _overlay.Show(WordOverlaySession.Start(
                target,
                screenWords,
                underlineSegmentIds: Array.Empty<string>()));

            var phraseRun = await _phraseOrchestrator!
                .AnalyzeAsync(recognitionId, result.Lines, cancellationToken)
                .ConfigureAwait(false);

            if (!IsCurrent(generation))
                return result;

            var phraseGroups = phraseRun.Groups
                .Select(group => ToScreen(group, result.FrameWidth, result.FrameHeight, target.Bounds))
                .ToArray();
            _diagnostics?.RecordPhraseAnalysis(recognitionId, phraseRun.Outcome, phraseRun.Groups, phraseRun.Warning);
            _overlay.Show(WordOverlaySession.Start(
                target,
                screenWords,
                phraseGroups,
                phraseRun.Warning,
                phraseRun.Words,
                phraseRun.SuccessfulSegmentIds));
            return result;
        }

        if (!IsCurrent(generation))
            return result;
        _overlay.Show(WordOverlaySession.Start(target, screenWords));
        return result;
    }

    public void Hide()
    {
        Interlocked.Increment(ref _recognitionGeneration);
        _overlay.Hide();
    }

    private bool IsCurrent(int generation)
        => Volatile.Read(ref _recognitionGeneration) == generation;

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
                    part.Rects.Select(rect => CoordinateMapper.ToScreen(rect, frameWidth, frameHeight, windowBounds)).ToArray())
                {
                    Surface = part.Surface,
                    Reading = part.Reading,
                    PitchAccents = part.PitchAccents,
                })
                .ToArray());
}

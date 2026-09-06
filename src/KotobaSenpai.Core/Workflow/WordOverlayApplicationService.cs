using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Diagnostics;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Core.Workflow;

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
    private readonly ITargetWindowTracker? _tracker;
    private int _recognitionGeneration;
    private string? _lastRecognizedText;

    public WordOverlayApplicationService(
        IWindowWordRecognizer recognizer,
        IOcrWordGroupingService grouping,
        IOverlayRenderer overlay,
        IDiagnosticReporter? diagnostics = null,
        PhraseAnalysisOrchestrator? phraseOrchestrator = null,
        ISettingsService? settings = null,
        ITargetWindowTracker? tracker = null)
    {
        _recognizer = recognizer;
        _grouping = grouping;
        _overlay = overlay;
        _diagnostics = diagnostics;
        _phraseOrchestrator = phraseOrchestrator;
        _settings = settings;
        _tracker = tracker;
    }

    public async Task<WordRecognitionResult> RecognizeAndShowAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        var recognitionId = Guid.NewGuid();
        var generation = Interlocked.Increment(ref _recognitionGeneration);
        var activeTarget = ValidateTarget(target);
        _phraseOrchestrator?.ClearCache();
        _overlay.Hide();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(generation))
            return result;

        _lastRecognizedText = GetRecognizedText(result.Lines);
        await PublishAsync(recognitionId, generation, activeTarget, result, incremental: false, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>Recognizes a changed frame and chooses incremental or full replacement from the OCR text relationship.</summary>
    public async Task<WordRecognitionResult?> RecognizeChangedAndShowAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        var observedGeneration = Volatile.Read(ref _recognitionGeneration);
        var activeTarget = ValidateTarget(target);
        var recognitionId = Guid.NewGuid();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(observedGeneration))
            return result;

        var newText = GetRecognizedText(result.Lines);
        if (string.Equals(_lastRecognizedText, newText, StringComparison.Ordinal))
            return result;

        var incremental = !string.IsNullOrEmpty(_lastRecognizedText)
            && newText.Contains(_lastRecognizedText, StringComparison.Ordinal);
        var generation = Interlocked.Increment(ref _recognitionGeneration);
        if (!incremental)
        {
            _phraseOrchestrator?.ClearCache();
            _overlay.Hide();
        }

        _lastRecognizedText = newText;
        await PublishAsync(recognitionId, generation, activeTarget, result, incremental, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<WordRecognitionResult> RecognizeCoreAsync(
        Guid recognitionId,
        WindowTarget activeTarget,
        CancellationToken cancellationToken)
    {
        var regionPixels = ReadRegionPixels(activeTarget);
        return await _recognizer.RecognizeAsync(recognitionId, activeTarget, cancellationToken, regionPixels)
            .ConfigureAwait(false);
    }

    private async Task PublishAsync(
        Guid recognitionId,
        int generation,
        WindowTarget activeTarget,
        WordRecognitionResult result,
        bool incremental,
        CancellationToken cancellationToken)
    {
        // First regroup characters into words in the frame coordinate system via the tokenizer (pure logic, coordinates unchanged), then map each word's union box to screen.
        var screenWords = _grouping.Group(result.Lines)
            .Select(word =>
            {
                var map = (ScreenRect rect) =>
                    CoordinateMapper.ToScreen(rect, result.FrameWidth, result.FrameHeight, activeTarget.Bounds);
                var rects = word.Rects.Select(map).ToArray();
                var sourceTokenLines = word.SourceTokenLines
                    .Select(lines => (IReadOnlyList<SourceTokenLineGeometry>)lines
                        .Select(line => line with { Bounds = map(line.Bounds) })
                        .ToArray())
                    .ToArray();
                return word.WithRects(rects, sourceTokenLines);
            })
            .ToArray();

        _diagnostics?.RecordTokens(recognitionId, activeTarget, screenWords);

        // Publish local furigana before the optional provider call. The empty set intentionally suppresses underlines
        // only while phrase analysis is enabled; the disabled path below keeps the legacy all-local overlay.
        var phraseEnabled = _phraseOrchestrator is not null && IsPhraseEnabled();
        if (phraseEnabled)
        {
            if (!IsCurrent(generation))
                return;
            if (!incremental)
                _overlay.Show(WordOverlaySession.Start(
                    activeTarget,
                    screenWords,
                    underlineSegmentIds: Array.Empty<string>()));

            var phraseRun = await _phraseOrchestrator!
                .AnalyzeAsync(recognitionId, result.Lines, cancellationToken)
                .ConfigureAwait(false);

            if (!IsCurrent(generation))
                return;

            var phraseGroups = phraseRun.Groups
                .Select(group => ToScreen(group, result.FrameWidth, result.FrameHeight, activeTarget.Bounds))
                .ToArray();
            _diagnostics?.RecordPhraseAnalysis(recognitionId, phraseRun.Outcome, phraseRun.Groups, phraseRun.Warning);
            _overlay.Show(WordOverlaySession.Start(
                activeTarget,
                screenWords,
                phraseGroups,
                phraseRun.Warning,
                phraseRun.Words,
                phraseRun.SuccessfulSegmentIds));
            return;
        }

        if (!IsCurrent(generation))
            return;
        _overlay.Show(WordOverlaySession.Start(activeTarget, screenWords));
    }

    public void Hide()
    {
        Interlocked.Increment(ref _recognitionGeneration);
        _lastRecognizedText = null;
        _overlay.Hide();
    }

    private bool IsCurrent(int generation)
        => Volatile.Read(ref _recognitionGeneration) == generation;

    private WindowTarget ValidateTarget(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_tracker is null)
            return target;

        var current = _tracker.Current;
        if (current is null || current.Handle != target.Handle)
            throw new BusinessRuleValidationException(
                ErrorCodes.TargetWindowUnavailable,
                "The selected target window is not currently tracked.");

        var snapshot = _tracker.Refresh();
        if (!snapshot.IsCapturable)
            throw new BusinessRuleValidationException(
                ErrorCodes.TargetWindowUnavailable,
                "The target window must be visible, restored, and unobstructed before recognition.");
        return snapshot.Target;
    }

    private static string GetRecognizedText(IReadOnlyList<OcrLine> lines)
        => string.Concat(lines.Select(line => line.Text));

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

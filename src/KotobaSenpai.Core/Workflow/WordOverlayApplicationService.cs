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
public sealed class WordOverlayApplicationService : IDisposable
{
    private readonly IWindowWordRecognizer _recognizer;
    private readonly IOcrWordGroupingService _grouping;
    private readonly IOverlayRenderer _overlay;
    private readonly IDiagnosticReporter? _diagnostics;
    private readonly PhraseAnalysisOrchestrator _phraseOrchestrator;
    private readonly ISettingsService? _settings;
    private readonly ITargetWindowTracker? _tracker;
    private readonly IExplanationLanguagePreferences? _explanationLanguage;
    private readonly SentenceTranslationPreferences? _sentenceTranslation;
    private int _recognitionGeneration;
    private string? _lastRecognizedText;
    private readonly object _publicationGate = new();
    private QueuedPublication? _pendingPublication;
    private bool _publishing;

    private sealed record PreparedPublication(Guid RecognitionId, int Generation, WindowTarget Target,
        WordRecognitionResult Result, IReadOnlyList<GroupedWord> Words);
    private sealed record QueuedPublication(PreparedPublication Prepared, CancellationToken CancellationToken,
        TaskCompletionSource<bool> Completion);

    public WordOverlayApplicationService(
        IWindowWordRecognizer recognizer,
        IOcrWordGroupingService grouping,
        IOverlayRenderer overlay,
        PhraseAnalysisOrchestrator phraseOrchestrator,
        IDiagnosticReporter? diagnostics = null,
        ISettingsService? settings = null,
        ITargetWindowTracker? tracker = null,
        IExplanationLanguagePreferences? explanationLanguage = null,
        SentenceTranslationPreferences? sentenceTranslation = null)
    {
        _recognizer = recognizer;
        _grouping = grouping;
        _overlay = overlay;
        _diagnostics = diagnostics;
        _phraseOrchestrator = phraseOrchestrator ?? throw new ArgumentNullException(nameof(phraseOrchestrator));
        _settings = settings;
        _tracker = tracker;
        _explanationLanguage = explanationLanguage;
        _sentenceTranslation = sentenceTranslation;
        if (_sentenceTranslation is not null)
            _sentenceTranslation.Changed += OnSentenceTranslationChanged;
        if (_explanationLanguage is not null)
            _explanationLanguage.EffectiveLanguageChanged += OnExplanationLanguageChanged;
    }

    private void OnSentenceTranslationChanged(object? sender, EventArgs e) => _lastRecognizedText = null;

    private void OnExplanationLanguageChanged(object? sender, EventArgs e)
    {
        // ponytail: in-flight requests finish normally; add cancellation only if mid-analysis language switching needs support.
        _phraseOrchestrator?.ClearCache();
        _lastRecognizedText = null;
    }

    public void Dispose()
    {
        if (_sentenceTranslation is not null)
            _sentenceTranslation.Changed -= OnSentenceTranslationChanged;
        if (_explanationLanguage is not null)
            _explanationLanguage.EffectiveLanguageChanged -= OnExplanationLanguageChanged;
    }

    public async Task<WordRecognitionResult> RecognizeAndShowAsync(
        WindowTarget target,
        CancellationToken cancellationToken = default)
    {
        var recognitionId = Guid.NewGuid();
        var generation = Interlocked.Increment(ref _recognitionGeneration);
        var activeTarget = ValidateTarget(target);
        _phraseOrchestrator.ClearCache();
        _overlay.Hide();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(generation))
            return result;

        _lastRecognizedText = GetRecognizedText(result.Lines);
        await PublishAsync(recognitionId, generation, activeTarget, result, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>Confirms local OCR and schedules cloud publication without blocking subsequent observations.</summary>
    public async Task<RecognitionUpdate?> BeginChangedAndShowAsync(WindowTarget target, CancellationToken cancellationToken = default)
    {
        var observedGeneration = Volatile.Read(ref _recognitionGeneration);
        var activeTarget = ValidateTarget(target);
        var recognitionId = Guid.NewGuid();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(observedGeneration))
            return new RecognitionUpdate(result, Task.FromResult(false));

        var newText = GetRecognizedText(result.Lines);
        if (string.Equals(_lastRecognizedText, newText, StringComparison.Ordinal))
            return new RecognitionUpdate(result, Task.FromResult(true));

        var generation = Interlocked.Increment(ref _recognitionGeneration);

        _lastRecognizedText = newText;
        var prepared = PreparePublication(recognitionId, generation, activeTarget, result);
        ShowPreview(prepared, cancellationToken);
        var queued = new QueuedPublication(prepared, cancellationToken,
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        var startWorker = false;
        lock (_publicationGate)
        {
            _pendingPublication?.Completion.TrySetResult(false);
            _pendingPublication = queued;
            if (!_publishing) { _publishing = true; startWorker = true; }
        }
        if (startWorker) _ = PublishLatestAsync();
        return new RecognitionUpdate(result, queued.Completion.Task);
    }

    /// <summary>Waits for both local confirmation and the queued cloud publication.</summary>
    public async Task<WordRecognitionResult?> RecognizeChangedAndShowAsync(WindowTarget target, CancellationToken cancellationToken = default)
    {
        var update = await BeginChangedAndShowAsync(target, cancellationToken).ConfigureAwait(false);
        if (update is null) return null;
        await update.Completion.ConfigureAwait(false);
        return update.Result;
    }

    private async Task PublishLatestAsync()
    {
        var refreshPreview = false;
        while (true)
        {
            QueuedPublication? queued;
            lock (_publicationGate)
            {
                queued = _pendingPublication;
                _pendingPublication = null;
                if (queued is null) { _publishing = false; return; }
            }
            try
            {
                queued.CancellationToken.ThrowIfCancellationRequested();
                // A waiting snapshot can now reuse sentences completed by the preceding batch.
                if (refreshPreview) ShowPreview(queued.Prepared, queued.CancellationToken);
                await AnalyzeAndPublishAsync(queued.Prepared, queued.CancellationToken).ConfigureAwait(false);
                queued.CancellationToken.ThrowIfCancellationRequested();
                queued.Completion.TrySetResult(IsCurrent(queued.Prepared.Generation));
            }
            catch (OperationCanceledException exception)
            {
                queued.Completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                queued.Completion.TrySetException(exception);
            }
            refreshPreview = true;
        }
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

    /// <summary>Checks the current frame and reuses successful analysis without clearing its cache.</summary>
    public async Task<WordRecognitionResult> ResumeAndShowAsync(WindowTarget target, CancellationToken cancellationToken = default)
    {
        var activeTarget = ValidateTarget(target);
        if (_tracker is not null && !_tracker.Refresh().IsRenderable)
            throw new BusinessRuleValidationException(ErrorCodes.TargetWindowUnavailable,
                "The target window must be renderable before resuming reading.");
        var generation = Interlocked.Increment(ref _recognitionGeneration);
        var recognitionId = Guid.NewGuid();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(generation)) return result;
        _lastRecognizedText = GetRecognizedText(result.Lines);
        await PublishAsync(recognitionId, generation, activeTarget, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public void Suspend()
    {
        Interlocked.Increment(ref _recognitionGeneration);
        _overlay.Suspend();
    }

    private Task PublishAsync(
        Guid recognitionId,
        int generation,
        WindowTarget activeTarget,
        WordRecognitionResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = PreparePublication(recognitionId, generation, activeTarget, result);
        ShowPreview(prepared, cancellationToken);
        return AnalyzeAndPublishAsync(prepared, cancellationToken);
    }

    private PreparedPublication PreparePublication(Guid recognitionId, int generation, WindowTarget activeTarget, WordRecognitionResult result)
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
        return new PreparedPublication(recognitionId, generation, activeTarget, result, screenWords);
    }

    private void ShowPreview(PreparedPublication prepared, CancellationToken cancellationToken)
    {
        var (recognitionId, generation, activeTarget, result, screenWords) = prepared;
        bool CanRender() => !cancellationToken.IsCancellationRequested && IsCurrent(generation);
        // Reproject cached annotations immediately; an OCR correction in one segment must not erase the others.
        if (!IsCurrent(generation))
            return;
        var cachedRun = _phraseOrchestrator.GetCachedAnalysis(recognitionId, result.Lines);
        _overlay.Show(WordOverlaySession.Start(
            activeTarget,
            screenWords,
            cachedRun.Groups.Select(group => ToScreen(group, result.FrameWidth, result.FrameHeight, activeTarget.Bounds)).ToArray(),
            cachedRun.Warning,
            cachedRun.Words,
            cachedRun.SuccessfulSegmentIds,
            cachedRun.Sentences), CanRender);
    }

    private async Task AnalyzeAndPublishAsync(PreparedPublication prepared, CancellationToken cancellationToken)
    {
        var (recognitionId, generation, activeTarget, result, screenWords) = prepared;
        bool CanRender() => !cancellationToken.IsCancellationRequested && IsCurrent(generation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(generation)) return;
        var phraseRun = await _phraseOrchestrator
            .AnalyzeAsync(recognitionId, result.Lines, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
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
            phraseRun.SuccessfulSegmentIds,
            phraseRun.Sentences), CanRender);
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
                "The target window must be visible and restored before recognition.");
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
                .ToArray()) { SegmentId = group.SegmentId };
}

/// <summary>A confirmed local snapshot and its tracked publication; false completion means it was superseded.</summary>
public sealed record RecognitionUpdate(WordRecognitionResult Result, Task<bool> Completion);

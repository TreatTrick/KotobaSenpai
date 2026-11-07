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
using KotobaSenpai.Core.Session;

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
    private readonly SessionEpoch _sessionEpoch;
    private string? _lastRecognizedText;
    private readonly object _publicationGate = new();
    private QueuedPublication? _pendingPublication;
    private bool _publishing;

    private sealed record PreparedPublication(Guid RecognitionId, SessionEpochSnapshot Epoch, WindowTarget Target,
        WordRecognitionResult Result, IReadOnlyList<GroupedWord> Words, PhraseAnalysisPlan Analysis);
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
        _sessionEpoch = phraseOrchestrator.SessionEpoch;
        _settings = settings;
        _tracker = tracker;
        _explanationLanguage = explanationLanguage;
        _sentenceTranslation = sentenceTranslation;
        if (_sentenceTranslation is not null)
            _sentenceTranslation.Changed += OnSentenceTranslationChanged;
        if (_explanationLanguage is not null)
            _explanationLanguage.EffectiveLanguageChanged += OnExplanationLanguageChanged;
    }

    internal SessionEpoch SessionEpoch => _sessionEpoch;

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
        var activeTarget = ValidateTarget(target);
        var epoch = _phraseOrchestrator.ClearCacheAndCaptureEpoch();
        _overlay.Hide();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(epoch))
            return result;

        _lastRecognizedText = GetRecognizedText(result.Lines);
        await PublishAsync(recognitionId, epoch, activeTarget, result, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <summary>Confirms local OCR and schedules cloud publication without blocking subsequent observations.</summary>
    public async Task<RecognitionUpdate?> BeginChangedAndShowAsync(WindowTarget target, CancellationToken cancellationToken = default)
    {
        var observedEpoch = _sessionEpoch.Capture();
        var activeTarget = ValidateTarget(target);
        var recognitionId = Guid.NewGuid();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(observedEpoch))
            return new RecognitionUpdate(result, Task.FromResult(false)) { Epoch = observedEpoch };

        var newText = GetRecognizedText(result.Lines);
        if (string.Equals(_lastRecognizedText, newText, StringComparison.Ordinal))
            return new RecognitionUpdate(result, Task.FromResult(true)) { Epoch = observedEpoch };

        if (!_sessionEpoch.TryAdvancePublication(observedEpoch, out var epoch))
            return new RecognitionUpdate(result, Task.FromResult(false)) { Epoch = epoch };

        _lastRecognizedText = newText;
        var prepared = PreparePublication(recognitionId, epoch, activeTarget, result);
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
        return new RecognitionUpdate(result, queued.Completion.Task) { Epoch = epoch };
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
                // Rebuild the shared layout after the preceding batch establishes new completed boundaries.
                var prepared = refreshPreview ? PreparePublication(queued.Prepared.RecognitionId, queued.Prepared.Epoch,
                    queued.Prepared.Target, queued.Prepared.Result) : queued.Prepared;
                if (refreshPreview) ShowPreview(prepared, queued.CancellationToken);
                await AnalyzeAndPublishAsync(prepared, queued.CancellationToken).ConfigureAwait(false);
                queued.CancellationToken.ThrowIfCancellationRequested();
                queued.Completion.TrySetResult(IsCurrent(queued.Prepared.Epoch));
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
        var epoch = _sessionEpoch.Advance();
        var recognitionId = Guid.NewGuid();
        var result = await RecognizeCoreAsync(recognitionId, activeTarget, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(epoch)) return result;
        _lastRecognizedText = GetRecognizedText(result.Lines);
        await PublishAsync(recognitionId, epoch, activeTarget, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public void Suspend()
    {
        _sessionEpoch.Advance();
        _overlay.Suspend();
    }

    private Task PublishAsync(
        Guid recognitionId,
        SessionEpochSnapshot epoch,
        WindowTarget activeTarget,
        WordRecognitionResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prepared = PreparePublication(recognitionId, epoch, activeTarget, result);
        ShowPreview(prepared, cancellationToken);
        return AnalyzeAndPublishAsync(prepared, cancellationToken);
    }

    private PreparedPublication PreparePublication(Guid recognitionId, SessionEpochSnapshot epoch, WindowTarget activeTarget, WordRecognitionResult result)
    {
        // First regroup characters into words in the frame coordinate system via the tokenizer (pure logic, coordinates unchanged), then map each word's union box to screen.
        var analysis = _phraseOrchestrator.PrepareAnalysis(recognitionId, result.Lines);
        var screenWords = _grouping.Group(analysis.Lines, analysis.Segments)
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
        return new PreparedPublication(recognitionId, epoch, activeTarget, result, screenWords, analysis);
    }

    private void ShowPreview(PreparedPublication prepared, CancellationToken cancellationToken)
    {
        var (_, epoch, activeTarget, result, screenWords, analysis) = prepared;
        bool CanRender() => !cancellationToken.IsCancellationRequested && IsCurrent(epoch);
        // Reproject cached annotations immediately; an OCR correction in one segment must not erase the others.
        if (!IsCurrent(epoch))
            return;
        var cachedRun = _phraseOrchestrator.GetCachedAnalysis(analysis);
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
        var (recognitionId, epoch, activeTarget, result, screenWords, analysis) = prepared;
        bool CanRender() => !cancellationToken.IsCancellationRequested && IsCurrent(epoch);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(epoch)) return;
        var phraseRun = await _phraseOrchestrator
            .AnalyzeAsync(analysis, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(epoch))
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
        _sessionEpoch.Advance();
        _lastRecognizedText = null;
        _overlay.Hide();
    }

    private bool IsCurrent(SessionEpochSnapshot epoch) => _sessionEpoch.IsCurrent(epoch);

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
public sealed record RecognitionUpdate(WordRecognitionResult Result, Task<bool> Completion)
{
    internal SessionEpochSnapshot Epoch { get; init; }
}

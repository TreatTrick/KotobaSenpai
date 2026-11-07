using System.Globalization;
using KotobaSenpai.Core.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Platform.Windows.Capture;

namespace KotobaSenpai.Platform.Windows.AutoRecognition;

/// <summary>Polls a capturable target region and invokes the application after a material pixel change.</summary>
public sealed class AutoRecognitionController : IAutoRecognitionController
{
    private readonly IWindowFrameCapture _capture;
    private readonly ITargetWindowTracker _tracker;
    private readonly ISettingsService _settings;
    private readonly IUiThread _uiThread;
    private readonly ILogger? _logger;
    private readonly Timer _timer;
    private int _tickPosted;
    private int _timerGeneration;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private WindowTarget? _target;
    private Func<CancellationToken, Task>? _onChange;
    private Baseline? _baseline;
    private double _differenceThreshold;
    private int _polling;
    private bool _running;
    private bool _disposed;

    private sealed record Baseline(nint Handle, ScreenRect? Region, int ClientWidth, int ClientHeight, CapturedFrame Frame);

    public AutoRecognitionController(
        IWindowFrameCapture capture,
        ITargetWindowTracker tracker,
        ISettingsService settings,
        IUiThread uiThread,
        ILogger? logger = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _uiThread = uiThread ?? throw new ArgumentNullException(nameof(uiThread));
        _timer = new Timer(_ => SchedulePoll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    public void Start(WindowTarget target, Func<CancellationToken, Task>? onChange = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        InvokeOnUiThread(() =>
        {
            ThrowIfDisposed();
            StopCore();
            lock (_gate)
            {
                _target = target;
                _onChange = onChange;
                _baseline = null;
                _differenceThreshold = ReadDifferenceThreshold();
                _cancellation = new CancellationTokenSource();
                _running = true;
            }

            var interval = ReadIntervalMilliseconds();
            _timer.Change(interval, interval);
        });
    }

    public void Stop()
        => InvokeOnUiThread(() => { if (!_disposed) StopCore(); });

    public void Dispose()
    {
        if (_uiThread.CheckAccess())
        {
            DisposeCore();
            return;
        }

        try
        {
            _uiThread.Invoke(DisposeCore);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to dispose auto-recognition controller.");
        }
    }

    internal static int NormalizeIntervalMilliseconds(string? raw)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval)
            ? Math.Max(AutoRecognitionSettings.MinimumIntervalMs, interval)
            : AutoRecognitionSettings.DefaultIntervalMs;

    internal void SchedulePoll()
    {
        var generation = Volatile.Read(ref _timerGeneration);
        if (!IsRunning) return;
        if (Interlocked.Exchange(ref _tickPosted, 1) != 0) return;
        try
        {
            _uiThread.Post(() =>
            {
                Interlocked.Exchange(ref _tickPosted, 0);
                if (!_disposed && generation == Volatile.Read(ref _timerGeneration))
                    _ = PollOnceAsync();
            });
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _tickPosted, 0);
            _logger?.LogError(exception, "Failed to schedule auto-recognition polling.");
        }
    }

    internal async Task PollOnceAsync()
    {
        if (!IsRunning || Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        var cancellationToken = new CancellationToken(true);
        try
        {
            WindowTarget? target;
            Func<CancellationToken, Task>? onChange;
            double threshold;
            lock (_gate)
            {
                target = _target;
                onChange = _onChange;
                cancellationToken = _cancellation?.Token ?? new CancellationToken(true);
                threshold = _differenceThreshold;
            }

            if (target is null || cancellationToken.IsCancellationRequested)
                return;

            var snapshot = _tracker.Current;
            if (snapshot is null || snapshot.Handle != target.Handle || !snapshot.IsCapturable)
            {
                ClearBaseline(cancellationToken);
                return;
            }

            var region = ReadRegionPixels(snapshot.Target);
            var frame = await _capture.CaptureAsync(snapshot.Target, cancellationToken, region);
            if (cancellationToken.IsCancellationRequested || !IsRunning)
                return;

            bool changed;
            lock (_gate)
            {
                if (_cancellation?.Token != cancellationToken || cancellationToken.IsCancellationRequested)
                    return;
                var baseline = _baseline;
                var previous = baseline is not null
                    && baseline.Handle == snapshot.Handle && baseline.Region == region
                    && baseline.ClientWidth == snapshot.Bounds.Width && baseline.ClientHeight == snapshot.Bounds.Height
                    && baseline.Frame.Width == frame.Width && baseline.Frame.Height == frame.Height
                    && baseline.Frame.CaptureGeneration == frame.CaptureGeneration
                    ? baseline.Frame : null;
                changed = FrameDifference.Exceeds(previous, frame, threshold);
                // Keep the last trigger as the baseline so slow typewriter updates can accumulate.
                if (previous is null || changed)
                    _baseline = new Baseline(snapshot.Handle, region, snapshot.Bounds.Width, snapshot.Bounds.Height, frame);
            }

            if (!changed || onChange is null)
                return;

            await onChange(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ClearBaseline(cancellationToken);
        }
        catch (Exception ex)
        {
            ClearBaseline(cancellationToken);
            _logger?.LogError(ex, "Auto-recognition polling failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void ClearBaseline(CancellationToken cancellationToken)
    {
        lock (_gate)
            if (_cancellation?.Token == cancellationToken) _baseline = null;
    }

    private int ReadIntervalMilliseconds()
        => NormalizeIntervalMilliseconds(_settings.GetValue(AutoRecognitionSettings.IntervalMsKey));

    private double ReadDifferenceThreshold()
    {
        if (!double.TryParse(
            _settings.GetValue(AutoRecognitionSettings.DifferenceThresholdKey),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var threshold)
            || !double.IsFinite(threshold))
            return AutoRecognitionSettings.DefaultDifferenceThreshold;
        return Math.Clamp(threshold, 0, 1);
    }

    private ScreenRect? ReadRegionPixels(WindowTarget target)
    {
        var raw = _settings.GetValue(RecognitionRegion.SettingsKey);
        return RecognitionRegion.TryParse(raw, out var region)
            ? region.ToPixelRect(target.Bounds.Width, target.Bounds.Height)
            : null;
    }

    private void StopCore()
    {
        Interlocked.Increment(ref _timerGeneration);
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        lock (_gate)
        {
            _running = false;
            _target = null;
            _onChange = null;
            _baseline = null;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void DisposeCore()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopCore();
        _timer.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AutoRecognitionController));
    }

    private void InvokeOnUiThread(Action action)
    {
        if (_uiThread.CheckAccess())
        {
            action();
            return;
        }

        _uiThread.Invoke(action);
    }
}

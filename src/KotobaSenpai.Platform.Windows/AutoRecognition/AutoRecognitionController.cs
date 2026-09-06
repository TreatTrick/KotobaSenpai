using System.Globalization;
using System.Windows.Threading;
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
    private readonly Dispatcher _dispatcher;
    private readonly ILogger? _logger;
    private readonly DispatcherTimer _timer;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private WindowTarget? _target;
    private Func<CancellationToken, Task>? _onChange;
    private CapturedFrame? _previousFrame;
    private double _differenceThreshold;
    private int _polling;
    private bool _running;
    private bool _disposed;

    public AutoRecognitionController(
        IWindowFrameCapture capture,
        ITargetWindowTracker tracker,
        ISettingsService settings,
        ILogger? logger = null,
        Dispatcher? dispatcher = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
        _timer.Tick += OnTimerTick;
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
        InvokeOnDispatcher(() =>
        {
            ThrowIfDisposed();
            StopCore();
            lock (_gate)
            {
                _target = target;
                _onChange = onChange;
                _previousFrame = null;
                _differenceThreshold = ReadDifferenceThreshold();
                _cancellation = new CancellationTokenSource();
                _running = true;
            }

            _timer.Interval = TimeSpan.FromMilliseconds(ReadIntervalMilliseconds());
            _timer.Start();
        });
    }

    public void Stop()
        => InvokeOnDispatcher(StopCore);

    public void Dispose()
    {
        if (_dispatcher.CheckAccess())
        {
            DisposeCore();
            return;
        }

        try
        {
            _dispatcher.Invoke(DisposeCore);
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

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _ = PollOnceAsync();
    }

    internal async Task PollOnceAsync()
    {
        if (!IsRunning || Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        try
        {
            WindowTarget? target;
            Func<CancellationToken, Task>? onChange;
            CancellationToken cancellationToken;
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
                return;

            var region = ReadRegionPixels(snapshot.Target);
            var frame = await _capture.CaptureAsync(snapshot.Target, cancellationToken, region);
            if (cancellationToken.IsCancellationRequested || !IsRunning)
                return;

            CapturedFrame? previous;
            lock (_gate)
            {
                previous = _previousFrame;
                _previousFrame = frame;
            }

            if (!FrameDifference.Exceeds(previous, frame, threshold) || onChange is null)
                return;

            await onChange(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Auto-recognition polling failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
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
        _timer.Stop();
        lock (_gate)
        {
            _running = false;
            _target = null;
            _onChange = null;
            _previousFrame = null;
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
        _timer.Tick -= OnTimerTick;
        StopCore();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AutoRecognitionController));
    }

    private void InvokeOnDispatcher(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }
}

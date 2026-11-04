using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;

namespace KotobaSenpai.Core.Workflow;

public enum RecognitionPhase { Idle, Recognizing, Ready, Hidden, Restoring, Stopped, Failed }

public sealed record RecognitionSessionState(
    RecognitionPhase Phase, bool AutoRecognitionEnabled, bool IsAutomaticRunning,
    int WordCount, Exception? Error);

public sealed record RecognitionCompleted(WordRecognitionResult Result, bool IsAutomatic);

/// <summary>Owns a reading session independently of its UI and keyboard entry points.</summary>
/// <remarks>Actions use one serialized caller context; asynchronous state notifications return to that context.</remarks>
public sealed class RecognitionSessionController : IDisposable
{
    private readonly WordOverlayApplicationService _workflow;
    private readonly ISettingsService _settings;
    private readonly IAutoRecognitionController? _automatic;
    private readonly ILogger? _logger;
    private readonly object _taskGate = new();
    private readonly HashSet<Task> _tasks = [];
    private CancellationTokenSource? _manualCancellation;
    private CancellationTokenSource? _automaticCancellation;
    private WindowTarget? _target;
    private int _version;
    private bool _disposed;
    private RecognitionPhase _phase;
    private bool _autoEnabled;
    private int _wordCount;
    private Exception? _error;

    public RecognitionSessionController(WordOverlayApplicationService workflow, ISettingsService settings,
        IAutoRecognitionController? automatic = null, ILogger? logger = null)
    {
        _workflow = workflow;
        _settings = settings;
        _automatic = automatic;
        _logger = logger;
        _autoEnabled = string.Equals(settings.GetValue(AutoRecognitionSettings.EnabledKey), "true", StringComparison.OrdinalIgnoreCase);
    }

    public event EventHandler? Changed;
    public event EventHandler<RecognitionCompleted>? Completed;
    public RecognitionSessionState State => new(_phase, _autoEnabled, _automatic?.IsRunning == true, _wordCount, _error);
    public bool IsBusy => _manualCancellation is not null;
    public bool HasSession => _target is not null;

    public Task RecognizeAsync(WindowTarget target)
        => _phase == RecognitionPhase.Hidden ? Task.CompletedTask : BeginRecognition(target, false);

    public Task ResumeAsync(WindowTarget target)
        => _phase == RecognitionPhase.Hidden ? BeginRecognition(target, true) : Task.CompletedTask;

    private Task BeginRecognition(WindowTarget target, bool resume)
    {
        if (_disposed || IsBusy) return Task.CompletedTask;
        CancelAutomatic();
        _target = target;
        var version = Interlocked.Increment(ref _version);
        var cancellation = new CancellationTokenSource();
        _manualCancellation = cancellation;
        SetState(resume ? RecognitionPhase.Restoring : RecognitionPhase.Recognizing);
        return Track(RunManualAsync(target, resume, version, cancellation));
    }

    private async Task RunManualAsync(WindowTarget target, bool resume, int version, CancellationTokenSource cancellation)
    {
        try
        {
            var result = resume
                ? await _workflow.ResumeAndShowAsync(target, cancellation.Token)
                : await _workflow.RecognizeAndShowAsync(target, cancellation.Token);
            if (!IsCurrent(version, cancellation.Token)) return;
            _wordCount = result.Lines.Sum(line => line.Words.Count);
            _phase = RecognitionPhase.Ready;
            Completed?.Invoke(this, new(result, false));
            StartAutomatic();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || version != _version) { }
        catch (Exception exception)
        {
            if (version != _version || _disposed) return;
            if (resume)
            {
                _workflow.Suspend();
                SetState(RecognitionPhase.Hidden, exception);
            }
            else
            {
                EndSession(RecognitionPhase.Failed, exception);
            }
        }
        finally
        {
            if (ReferenceEquals(_manualCancellation, cancellation))
            {
                _manualCancellation = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            cancellation.Dispose();
        }
    }

    public async Task SetAutoRecognitionAsync(bool enabled, WindowTarget? selectedTarget)
    {
        if (_disposed) return;
        if (_autoEnabled != enabled)
        {
            _settings.SetValue(AutoRecognitionSettings.EnabledKey, enabled ? "true" : "false");
            _autoEnabled = enabled;
        }
        if (!enabled) CancelAutomatic();
        Changed?.Invoke(this, EventArgs.Empty);
        if (!enabled || _phase == RecognitionPhase.Hidden || IsBusy || selectedTarget is null) return;
        if (_phase == RecognitionPhase.Ready && _target?.Handle == selectedTarget.Handle)
        {
            StartAutomatic();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else await RecognizeAsync(selectedTarget);
    }

    public void PauseAndHide()
    {
        if (_disposed || !HasSession) return;
        Interlocked.Increment(ref _version);
        CancelManual();
        CancelAutomatic();
        _workflow.Suspend();
        SetState(RecognitionPhase.Hidden);
    }

    public void Stop() => EndSession(RecognitionPhase.Stopped);

    private void EndSession(RecognitionPhase phase, Exception? error = null)
    {
        Interlocked.Increment(ref _version);
        CancelManual();
        CancelAutomatic();
        _target = null;
        _workflow.Hide();
        SetState(phase, error);
    }

    /// <summary>Includes tasks launched by keyboard actions and automatic sampling.</summary>
    public async Task StopAsync()
    {
        Stop();
        Task[] pending;
        lock (_taskGate) pending = _tasks.ToArray();
        await Task.WhenAll(pending);
    }

    private void StartAutomatic()
    {
        var target = _target;
        if (_disposed || !_autoEnabled || _phase != RecognitionPhase.Ready || target is null || _automatic is null) return;
        _automatic.Start(target, token => Track(RunAutomaticAsync(target, token)));
    }

    private async Task RunAutomaticAsync(WindowTarget target, CancellationToken token)
    {
        if (_disposed || !_autoEnabled || _phase != RecognitionPhase.Ready || target.Handle != _target?.Handle) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _automaticCancellation = cancellation;
        var version = _version;
        try
        {
            var result = await _workflow.RecognizeChangedAndShowAsync(target, cancellation.Token);
            if (result is not null && IsCurrent(version, cancellation.Token))
            {
                _wordCount = result.Lines.Sum(line => line.Words.Count);
                Completed?.Invoke(this, new(result, true));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || version != _version) { }
        catch (Exception exception) { _logger?.LogError(exception, "Automatic recognition failed."); }
        finally
        {
            if (ReferenceEquals(_automaticCancellation, cancellation)) _automaticCancellation = null;
        }
    }

    private Task Track(Task task)
    {
        lock (_taskGate) _tasks.Add(task);
        _ = task.ContinueWith(completed => { lock (_taskGate) _tasks.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private bool IsCurrent(int version, CancellationToken token)
        => !_disposed && version == Volatile.Read(ref _version) && !token.IsCancellationRequested;

    private void CancelManual()
    {
        var cancellation = _manualCancellation;
        _manualCancellation = null;
        cancellation?.Cancel();
    }

    private void CancelAutomatic()
    {
        _automatic?.Stop();
        _automaticCancellation?.Cancel();
    }

    private void SetState(RecognitionPhase phase, Exception? error = null)
    {
        _phase = phase;
        _error = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

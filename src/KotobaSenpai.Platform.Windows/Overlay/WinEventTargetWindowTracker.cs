using KotobaSenpai.Core.TargetWindow;
using System.Windows.Threading;
using KotobaSenpai.Core.Logging;
using static KotobaSenpai.Platform.Windows.Overlay.WinEventHooks;
using static KotobaSenpai.Core.TargetWindow.WindowOcclusionGeometry;
using static KotobaSenpai.Platform.Windows.Overlay.TargetWindowSnapshotReader;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Win32 event-driven tracker for one target window.</summary>
public sealed class WinEventTargetWindowTracker : ITargetWindowTracker
{
    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly ILogger? _logger;
    private readonly TargetWindowSnapshotReader _snapshotReader;
    private WinEventHooks? _hooks;
    private nint _targetHandle;
    private uint _targetThreadId;
    private uint _targetProcessId;
    private volatile bool _targetDestroyed;
    private bool _refreshPosted;
    private bool _disposed;
    private TargetWindowSnapshot? _current;

    public WinEventTargetWindowTracker(ILogger? logger = null, Dispatcher? dispatcher = null)
    {
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _snapshotReader = new TargetWindowSnapshotReader(logger);
        var assembly = typeof(WinEventTargetWindowTracker).Assembly;
        _logger?.LogInformation("[OVERLAY] Diagnostics initialized: diagnosticsVersion=coverage30-v2, processId={0}, assemblyPath={1}, moduleVersionId={2}, occlusionRule=client-union-area, hideAboveRatio={3:R}.",
            Environment.ProcessId, assembly.Location, assembly.ManifestModule.ModuleVersionId, OcclusionHideThreshold);
    }

    public event EventHandler<TargetWindowSnapshot>? Changed;

    public TargetWindowSnapshot? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public TargetWindowSnapshot Attach(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _logger?.Log(
            LogLevel.Debug,
            null,
            "[DEBUG-TRACKER] Attach requested: handle={0}, title='{1}', bounds={2}",
            target.Handle,
            target.Title,
            target.Bounds);
        return InvokeOnDispatcher(() =>
        {
            ThrowIfDisposed();
            var threadId = WindowNativeMethods.GetWindowThreadProcessId(target.Handle, out var processId);
            if (_targetHandle == target.Handle && _hooks?.IsRegistered == true
                && IsSameIdentity(_targetThreadId, _targetProcessId, threadId, processId, _targetDestroyed))
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[DEBUG-TRACKER] Reusing registered hooks for handle={0}; refreshing snapshot.",
                    target.Handle);
                var currentSnapshot = _snapshotReader.Read(_targetHandle, target.Title, target.Bounds, _targetThreadId, _targetProcessId, _targetDestroyed);
                Publish(currentSnapshot);
                return currentSnapshot;
            }

            DetachCore();
            _targetHandle = target.Handle;
            _targetThreadId = threadId;
            _targetProcessId = processId;
            try
            {
                _hooks = new WinEventHooks(OnWinEvent, _targetHandle, _logger);
                var snapshot = _snapshotReader.Read(_targetHandle, target.Title, target.Bounds, _targetThreadId, _targetProcessId, _targetDestroyed);
                Publish(snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to register target window hooks");
                DetachCore();
                throw;
            }
        });
    }

    public TargetWindowSnapshot Refresh()
        => InvokeOnDispatcher(() =>
        {
            ThrowIfDisposed();
            var current = Current ?? throw new InvalidOperationException("No target window is attached.");
            _logger?.Log(
                LogLevel.Trace,
                null,
                "[TRACKER] Explicit refresh requested: handle={0}, title='{1}'.",
                current.Handle,
                current.Title);
            var snapshot = _snapshotReader.Read(_targetHandle, current.Title, current.Bounds, _targetThreadId, _targetProcessId, _targetDestroyed);
            Publish(snapshot);
            return snapshot;
        });

    public void Detach()
        => InvokeOnDispatcher(() =>
        {
            DetachCore();
            return 0;
        });

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
            _logger?.LogError(ex, "Failed to dispose target window tracker");
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _targetHandle == nint.Zero)
                    return;
                if (!IsRelevantEvent(eventType, hwnd, _targetHandle, idObject, idChild, OverlayWindowRegistry.Snapshot()))
                    return;
                if (eventType == EventObjectDestroy && hwnd == _targetHandle) _targetDestroyed = true;
                if (_refreshPosted)
                    return;
                _refreshPosted = true;
            }

            _logger?.Log(
                LogLevel.Trace,
                null,
                "[TRACKER] WinEvent accepted: eventType=0x{0:X8}, hwnd={1}, targetHandle={2}; refresh posted.",
                eventType,
                hwnd,
                _targetHandle);
            _dispatcher.BeginInvoke(RefreshFromEvent, DispatcherPriority.DataBind);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Target window event callback failed");
        }
    }

    internal static bool IsRelevantEvent(
        uint eventType,
        nint hwnd,
        nint targetHandle,
        int idObject,
        int idChild,
        IReadOnlySet<nint>? auxiliaryWindowHandles = null)
    {
        if (targetHandle == nint.Zero)
            return false;
        if (eventType == EventSystemForeground)
            return true;
        if (hwnd == nint.Zero || auxiliaryWindowHandles?.Contains(hwnd) == true)
            return false;
        if (eventType is EventSystemMinimizeStart or EventSystemMinimizeEnd)
            return true;
        return eventType is EventObjectDestroy or EventObjectShow or EventObjectHide or EventObjectReorder or EventObjectLocationChange
            && idObject == ObjectIdWindow
            && idChild == ChildIdSelf;
    }

    private void RefreshFromEvent()
    {
        lock (_gate)
            _refreshPosted = false;

        try
        {
            var current = Current;
            if (current is null || _targetHandle == nint.Zero)
                return;
            var snapshot = _snapshotReader.Read(_targetHandle, current.Title, current.Bounds, _targetThreadId, _targetProcessId, _targetDestroyed);
            Publish(snapshot);
            if (!snapshot.IsValid)
                DetachCore();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Target window snapshot refresh failed");
        }
    }

    private void Publish(TargetWindowSnapshot snapshot)
    {
        TargetWindowSnapshot? previous;
        lock (_gate)
        {
            previous = _current;
            _current = snapshot;
        }
        if (previous != snapshot)
            _logger?.Log(LogLevel.Trace, null, "[OVERLAY] Target snapshot changed: handle={0}, bounds={1}, dpi={2:0.###}, valid={3}, visible={4}, minimized={5}, foreground={6}, occluded={7}, capturable={8}, renderable={9}.",
                snapshot.Handle, snapshot.Bounds, snapshot.DpiScale, snapshot.IsValid, snapshot.IsVisible, snapshot.IsMinimized,
                snapshot.IsForeground, snapshot.IsOccluded, snapshot.IsCapturable, snapshot.IsRenderable);
        _logger?.Log(
            LogLevel.Trace,
            null,
            "[TRACKER] Snapshot published: handle={0}, title='{1}', bounds={2}, dpi={3:0.###}, visible={4}, minimized={5}, foreground={6}, occluded={7}, capturable={8}, renderable={9}.",
            snapshot.Handle,
            snapshot.Title,
            snapshot.Bounds,
            snapshot.DpiScale,
            snapshot.IsVisible,
            snapshot.IsMinimized,
            snapshot.IsForeground,
            snapshot.IsOccluded,
            snapshot.IsCapturable,
            snapshot.IsRenderable);
        try
        {
            Changed?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Target window change notification failed");
        }
    }

    private void DetachCore()
    {
        if (_targetHandle != nint.Zero || _hooks?.IsRegistered == true)
        {
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-TRACKER] Detaching target: handle={0}, hooksRegistered={1}.",
                _targetHandle,
                _hooks?.IsRegistered == true);
        }
        _hooks?.Dispose();
        _hooks = null;
        lock (_gate)
        {
            _targetHandle = nint.Zero;
            _targetThreadId = 0;
            _targetProcessId = 0;
            _targetDestroyed = false;
            _snapshotReader.Reset();
            _current = null;
            _refreshPosted = false;
        }
    }

    private void DisposeCore()
    {
        if (_disposed)
            return;
        _disposed = true;
        DetachCore();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WinEventTargetWindowTracker));
    }

    private T InvokeOnDispatcher<T>(Func<T> action)
        => _dispatcher.CheckAccess() ? action() : _dispatcher.Invoke(action);
}

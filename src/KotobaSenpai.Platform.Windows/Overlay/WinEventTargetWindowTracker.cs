using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.Platform.Windows.Overlay;

internal static class OverlayWindowRegistry
{
    private static readonly object Gate = new();
    private static readonly HashSet<nint> Handles = new();

    public static void Register(nint handle)
    {
        if (handle == nint.Zero)
            return;
        lock (Gate)
            Handles.Add(handle);
    }

    public static void Unregister(nint handle)
    {
        if (handle == nint.Zero)
            return;
        lock (Gate)
            Handles.Remove(handle);
    }

    public static IReadOnlySet<nint> Snapshot()
    {
        lock (Gate)
            return Handles.ToHashSet();
    }
}

/// <summary>Win32 event-driven tracker for one target window.</summary>
public sealed class WinEventTargetWindowTracker : ITargetWindowTracker
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectReorder = 0x8004;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint WineventOutOfContext = 0;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint GwHwndPrev = 3;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000;
    private const long WsExTransparent = 0x00000020;
    private const double OcclusionHideThreshold = 0.30;

    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly ILogger? _logger;
    private readonly NativeMethods.WinEventDelegate _callback;
    private nint _locationHook;
    private nint _systemHook;
    private nint _destroyHook;
    private nint _visibilityHook;
    private nint _reorderHook;
    private nint _foregroundHook;
    private nint _targetHandle;
    private string? _lastOcclusionDiagnosticState;
    private bool _refreshPosted;
    private bool _disposed;
    private TargetWindowSnapshot? _current;

    public WinEventTargetWindowTracker(ILogger? logger = null, Dispatcher? dispatcher = null)
    {
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _callback = OnWinEvent;
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
            if (_targetHandle == target.Handle && HooksRegistered())
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[DEBUG-TRACKER] Reusing registered hooks for handle={0}; refreshing snapshot.",
                    target.Handle);
                var currentSnapshot = RefreshCore(target.Title, target.Bounds);
                Publish(currentSnapshot);
                return currentSnapshot;
            }

            DetachCore();
            _targetHandle = target.Handle;
            try
            {
                RegisterHooks();
                var snapshot = RefreshCore(target.Title, target.Bounds);
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
            var snapshot = RefreshCore(current.Title, current.Bounds);
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
            var snapshot = RefreshCore(current.Title, current.Bounds);
            Publish(snapshot);
            if (!snapshot.IsValid)
                DetachCore();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Target window snapshot refresh failed");
        }
    }

    private TargetWindowSnapshot RefreshCore(string title, ScreenRect fallbackBounds)
    {
        if (!NativeMethods.IsWindow(_targetHandle))
            return new TargetWindowSnapshot(_targetHandle, title, fallbackBounds, 1, false, true, false, false, false);

        var visible = NativeMethods.IsWindowVisible(_targetHandle);
        var minimized = NativeMethods.IsIconic(_targetHandle);
        if (!NativeMethods.GetClientRect(_targetHandle, out var rect)
            || !NativeMethods.ClientToScreen(_targetHandle, out var origin)
            || rect.Right <= rect.Left
            || rect.Bottom <= rect.Top)
            return new TargetWindowSnapshot(_targetHandle, title, fallbackBounds, ReadDpiScale(), visible, minimized, false);

        var bounds = new ScreenRect(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return new TargetWindowSnapshot(
            _targetHandle,
            title,
            bounds,
            ReadDpiScale(),
            visible,
            minimized,
            NativeMethods.GetForegroundWindow() == _targetHandle,
            IsOccluded(bounds, _targetHandle));
    }

    private bool IsOccluded(ScreenRect targetBounds, nint targetHandle)
    {
        var auxiliaryWindowHandles = OverlayWindowRegistry.Snapshot();
        var occluders = new List<ScreenRect>();
        var candidates = new List<(nint Handle, ScreenRect Bounds, int ExtendedStyle, string Exclusion)>();
        for (var above = NativeMethods.GetWindow(targetHandle, GwHwndPrev);
             above != nint.Zero;
             above = NativeMethods.GetWindow(above, GwHwndPrev))
        {
            if (!NativeMethods.IsWindowVisible(above)
                || NativeMethods.IsIconic(above)
                || !NativeMethods.GetWindowRect(above, out var rect)
                || rect.Right <= rect.Left
                || rect.Bottom <= rect.Top)
                continue;

            var occluderBounds = new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            if (!HasPositiveAreaIntersection(targetBounds, occluderBounds))
                continue;
            var extendedStyle = NativeMethods.GetWindowLong(above, GwlExStyle);
            var exclusion = auxiliaryWindowHandles.Contains(above) ? "auxiliary"
                : !CanOcclude(extendedStyle) ? "layered-click-through" : "none";
            candidates.Add((above, occluderBounds, extendedStyle, exclusion));
            if (exclusion == "none")
                occluders.Add(occluderBounds);
        }

        var occluded = ExceedsOcclusionThreshold(targetBounds, occluders, out var occlusionRatio);
        LogOcclusionEvaluation(targetHandle, targetBounds, candidates, occluders.Count, occlusionRatio, occluded);
        return occluded;
    }

    private void LogOcclusionEvaluation(nint targetHandle, ScreenRect targetBounds,
        IReadOnlyList<(nint Handle, ScreenRect Bounds, int ExtendedStyle, string Exclusion)> candidates,
        int occluderCount, double ratio, bool occluded)
    {
        if (_logger is null)
            return;
        var state = $"{targetBounds}|{string.Join(";", candidates.Select(window => $"{window.Handle}:{window.Bounds}:{window.ExtendedStyle:X}:{window.Exclusion}"))}";
        if (state == _lastOcclusionDiagnosticState)
            return;
        _lastOcclusionDiagnosticState = state;
        _logger.Log(LogLevel.Trace, null, "[OVERLAY] Occlusion evaluated: targetHandle={0}, targetBounds={1}, occlusionRatio={2:R}, hideAboveRatio={3:R}, occluded={4}, occluderCount={5}, intersectingWindowCount={6}.",
            targetHandle, targetBounds, ratio, OcclusionHideThreshold, occluded, occluderCount, candidates.Count);
        foreach (var window in candidates)
        {
            var className = new StringBuilder(256);
            NativeMethods.GetClassName(window.Handle, className, className.Capacity);
            NativeMethods.GetWindowThreadProcessId(window.Handle, out var processId);
            var cloakResult = NativeMethods.DwmGetWindowAttribute(window.Handle, 14, out var cloaked, sizeof(uint));
            _logger.Log(LogLevel.Trace, null, "[OVERLAY] Occlusion candidate: hwnd={0}, processId={1}, className={2}, exStyle=0x{3:X}, bounds={4}, clientIntersection={5}, exclusion={6}, cloaked={7}.",
                window.Handle, processId, className.ToString(), window.ExtendedStyle, window.Bounds,
                window.Bounds.ClampTo(targetBounds), window.Exclusion, cloakResult >= 0 ? cloaked.ToString() : "unknown");
        }
    }

    internal static bool HasPositiveAreaIntersection(ScreenRect first, ScreenRect second)
        => first.X < second.Right
            && first.Right > second.X
            && first.Y < second.Bottom
            && first.Bottom > second.Y;

    internal static bool ExceedsOcclusionThreshold(ScreenRect targetBounds, IEnumerable<ScreenRect> occluders, out double occlusionRatio)
    {
        var clipped = occluders.Where(bounds => HasPositiveAreaIntersection(targetBounds, bounds))
            .Select(bounds => bounds.ClampTo(targetBounds)).ToArray();
        var edges = clipped.SelectMany(bounds => new[] { bounds.X, bounds.Right }).Distinct().Order().ToArray();
        long coveredArea = 0;
        // ponytail: O(n^2 log n) for a small overlapping window set; use a region index only if profiling shows a bottleneck.
        for (var i = 1; i < edges.Length; i++)
        {
            long coveredHeight = 0;
            var bottom = int.MinValue;
            foreach (var bounds in clipped.Where(bounds => bounds.X < edges[i] && bounds.Right > edges[i - 1]).OrderBy(bounds => bounds.Y))
            {
                var top = Math.Max(bounds.Y, bottom);
                if (bounds.Bottom > top)
                    coveredHeight += (long)bounds.Bottom - top;
                bottom = Math.Max(bottom, bounds.Bottom);
            }
            coveredArea += (long)(edges[i] - edges[i - 1]) * coveredHeight;
        }
        occlusionRatio = coveredArea / ((double)targetBounds.Width * targetBounds.Height);
        return occlusionRatio > OcclusionHideThreshold;
    }

    internal static bool CanOcclude(long extendedStyle)
    {
        // A layered click-through helper can span transparent desktop areas; its outer rectangle is not a solid occluder.
        const long clickThroughLayered = WsExLayered | WsExTransparent;
        return (extendedStyle & clickThroughLayered) != clickThroughLayered;
    }

    private double ReadDpiScale()
    {
        var dpi = NativeMethods.GetDpiForWindow(_targetHandle);
        return dpi == 0 ? 1 : dpi / 96d;
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

    private void RegisterHooks()
    {
        _locationHook = NativeMethods.SetWinEventHook(
            EventObjectLocationChange,
            EventObjectLocationChange,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _systemHook = NativeMethods.SetWinEventHook(
            EventSystemMinimizeStart,
            EventSystemMinimizeEnd,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _destroyHook = NativeMethods.SetWinEventHook(
            EventObjectDestroy,
            EventObjectDestroy,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _visibilityHook = NativeMethods.SetWinEventHook(
            EventObjectShow,
            EventObjectHide,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _reorderHook = NativeMethods.SetWinEventHook(
            EventObjectReorder,
            EventObjectReorder,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        _foregroundHook = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);

        if (_locationHook == nint.Zero
            || _systemHook == nint.Zero
            || _destroyHook == nint.Zero
            || _visibilityHook == nint.Zero
            || _reorderHook == nint.Zero
            || _foregroundHook == nint.Zero)
            throw new InvalidOperationException("Failed to register one or more WinEvent hooks.");

        _logger?.Log(
            LogLevel.Debug,
            null,
            "[DEBUG-TRACKER] WinEvent hooks registered: targetHandle={0}, locationHook={1}, systemHook={2}, destroyHook={3}, visibilityHook={4}, reorderHook={5}, foregroundHook={6}.",
            _targetHandle,
            _locationHook,
            _systemHook,
            _destroyHook,
            _visibilityHook,
            _reorderHook,
            _foregroundHook);
    }

    private bool HooksRegistered()
        => _locationHook != nint.Zero
            && _systemHook != nint.Zero
            && _destroyHook != nint.Zero
            && _visibilityHook != nint.Zero
            && _reorderHook != nint.Zero
            && _foregroundHook != nint.Zero;

    private void DetachCore()
    {
        if (_targetHandle != nint.Zero || HooksRegistered())
        {
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-TRACKER] Detaching target: handle={0}, hooksRegistered={1}.",
                _targetHandle,
                HooksRegistered());
        }
        Unhook(ref _locationHook, "location");
        Unhook(ref _systemHook, "system");
        Unhook(ref _destroyHook, "destroy");
        Unhook(ref _visibilityHook, "visibility");
        Unhook(ref _reorderHook, "reorder");
        Unhook(ref _foregroundHook, "foreground");
        lock (_gate)
        {
            _targetHandle = nint.Zero;
            _lastOcclusionDiagnosticState = null;
            _current = null;
            _refreshPosted = false;
        }
    }

    private void Unhook(ref nint hook, string name)
    {
        if (hook == nint.Zero)
            return;
        try
        {
            if (!NativeMethods.UnhookWinEvent(hook))
                _logger?.LogWarning("Failed to unregister {0} WinEvent hook", name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to unregister {0} WinEvent hook", name);
        }
        finally
        {
            hook = nint.Zero;
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

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void WinEventDelegate(
            nint hook,
            uint eventType,
            nint hwnd,
            int idObject,
            int idChild,
            uint eventThread,
            uint eventTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern nint SetWinEventHook(
            uint eventMin,
            uint eventMax,
            nint hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(nint hook);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(nint hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(nint hwnd, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(nint hwnd, out Point point);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hwnd);

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern nint GetWindow(nint hwnd, uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint hwnd, out Rect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(nint hwnd, StringBuilder className, int maximumCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(nint hwnd, int index);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out uint value, int size);

    }
}

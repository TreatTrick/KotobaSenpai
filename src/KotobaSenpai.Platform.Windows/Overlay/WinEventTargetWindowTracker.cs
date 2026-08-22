using System.Runtime.InteropServices;
using System.Windows.Threading;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Win32 event-driven tracker for one target window.</summary>
public sealed class WinEventTargetWindowTracker : ITargetWindowTracker
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjectIdWindow = 0;
    private const int ChildIdSelf = 0;
    private const uint WineventOutOfContext = 0;
    private const uint WineventSkipOwnProcess = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint GwHwndPrev = 3;

    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly ILogger? _logger;
    private readonly NativeMethods.WinEventDelegate _callback;
    private nint _locationHook;
    private nint _systemHook;
    private nint _destroyHook;
    private nint _foregroundHook;
    private nint _targetHandle;
    private bool _refreshPosted;
    private bool _disposed;
    private TargetWindowSnapshot? _current;

    public WinEventTargetWindowTracker(ILogger? logger = null, Dispatcher? dispatcher = null)
    {
        _logger = logger;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _callback = OnWinEvent;
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
        return InvokeOnDispatcher(() =>
        {
            ThrowIfDisposed();
            if (_targetHandle == target.Handle && HooksRegistered())
            {
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
                if (!IsRelevantEvent(eventType, hwnd, _targetHandle, idObject, idChild))
                    return;
                if (_refreshPosted)
                    return;
                _refreshPosted = true;
            }

            _dispatcher.BeginInvoke(RefreshFromEvent, DispatcherPriority.DataBind);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Target window event callback failed");
        }
    }

    internal static bool IsRelevantEvent(uint eventType, nint hwnd, nint targetHandle, int idObject, int idChild)
        => eventType == EventSystemForeground
            || (hwnd != nint.Zero
                && hwnd == targetHandle
                && idObject == ObjectIdWindow
                && idChild == ChildIdSelf);

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
            if (!NativeMethods.IsWindow(_targetHandle))
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
            return new TargetWindowSnapshot(_targetHandle, title, fallbackBounds, 1, false, true, false);

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

    private static bool IsOccluded(ScreenRect targetBounds, nint targetHandle)
    {
        for (var above = NativeMethods.GetWindow(targetHandle, GwHwndPrev);
             above != nint.Zero;
             above = NativeMethods.GetWindow(above, GwHwndPrev))
        {
            if (NativeMethods.GetWindowThreadProcessId(above, out var processId) == 0
                || processId == Environment.ProcessId
                || !NativeMethods.IsWindowVisible(above)
                || NativeMethods.IsIconic(above)
                || !NativeMethods.GetWindowRect(above, out var rect))
                continue;

            if (rect.Left < targetBounds.Right
                && rect.Right > targetBounds.X
                && rect.Top < targetBounds.Bottom
                && rect.Bottom > targetBounds.Y)
                return true;
        }

        return false;
    }

    private double ReadDpiScale()
    {
        var dpi = NativeMethods.GetDpiForWindow(_targetHandle);
        return dpi == 0 ? 1 : dpi / 96d;
    }

    private void Publish(TargetWindowSnapshot snapshot)
    {
        lock (_gate)
            _current = snapshot;
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
            WineventOutOfContext | WineventSkipOwnProcess);
        _systemHook = NativeMethods.SetWinEventHook(
            EventSystemMinimizeStart,
            EventSystemMinimizeEnd,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        _destroyHook = NativeMethods.SetWinEventHook(
            EventObjectDestroy,
            EventObjectDestroy,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);
        _foregroundHook = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        if (_locationHook == nint.Zero || _systemHook == nint.Zero || _destroyHook == nint.Zero || _foregroundHook == nint.Zero)
            throw new InvalidOperationException("Failed to register one or more WinEvent hooks.");
    }

    private bool HooksRegistered()
        => _locationHook != nint.Zero
            && _systemHook != nint.Zero
            && _destroyHook != nint.Zero
            && _foregroundHook != nint.Zero;

    private void DetachCore()
    {
        Unhook(ref _locationHook, "location");
        Unhook(ref _systemHook, "system");
        Unhook(ref _destroyHook, "destroy");
        Unhook(ref _foregroundHook, "foreground");
        lock (_gate)
        {
            _targetHandle = nint.Zero;
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

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    }
}

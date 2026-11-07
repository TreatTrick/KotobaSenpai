using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>Keeps the callback alive and releases every hook, including partial registration failures.</summary>
internal sealed class WinEventHooks : IDisposable
{
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventSystemMinimizeStart = 0x0016;
    internal const uint EventSystemMinimizeEnd = 0x0017;
    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectReorder = 0x8004;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const int ObjectIdWindow = 0;
    internal const int ChildIdSelf = 0;

    private static readonly (uint First, uint Last, string Name)[] Events =
    [
        (EventObjectLocationChange, EventObjectLocationChange, "location"),
        (EventSystemMinimizeStart, EventSystemMinimizeEnd, "system"),
        (EventObjectDestroy, EventObjectDestroy, "destroy"),
        (EventObjectShow, EventObjectHide, "visibility"),
        (EventObjectReorder, EventObjectReorder, "reorder"),
        (EventSystemForeground, EventSystemForeground, "foreground"),
    ];

    private readonly WindowNativeMethods.WinEventDelegate _callback;
    private readonly ILogger? _logger;
    private readonly Func<nint, bool> _unregister;
    private readonly nint[] _handles = new nint[Events.Length];

    public WinEventHooks(WindowNativeMethods.WinEventDelegate callback, nint targetHandle, ILogger? logger)
        : this(callback, targetHandle, logger,
            static (first, last, handler) => WindowNativeMethods.SetWinEventHook(first, last, nint.Zero, handler, 0, 0, 0),
            WindowNativeMethods.UnhookWinEvent)
    {
    }

    internal WinEventHooks(WindowNativeMethods.WinEventDelegate callback, nint targetHandle, ILogger? logger,
        Func<uint, uint, WindowNativeMethods.WinEventDelegate, nint> register, Func<nint, bool> unregister)
    {
        _callback = callback;
        _logger = logger;
        _unregister = unregister;
        try
        {
            for (var i = 0; i < Events.Length; i++)
                _handles[i] = register(Events[i].First, Events[i].Last, _callback);
            if (!IsRegistered)
                throw new InvalidOperationException("Failed to register one or more WinEvent hooks.");
            _logger?.Log(LogLevel.Debug, null,
                "[DEBUG-TRACKER] WinEvent hooks registered: targetHandle={0}, locationHook={1}, systemHook={2}, destroyHook={3}, visibilityHook={4}, reorderHook={5}, foregroundHook={6}.",
                targetHandle, _handles[0], _handles[1], _handles[2], _handles[3], _handles[4], _handles[5]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsRegistered => _handles.All(handle => handle != nint.Zero);

    public void Dispose()
    {
        for (var i = 0; i < _handles.Length; i++)
        {
            if (_handles[i] == nint.Zero)
                continue;
            try
            {
                if (!_unregister(_handles[i]))
                    _logger?.LogWarning("Failed to unregister {0} WinEvent hook", Events[i].Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to unregister {0} WinEvent hook", Events[i].Name);
            }
            finally
            {
                _handles[i] = nint.Zero;
            }
        }
        GC.KeepAlive(_callback);
    }
}

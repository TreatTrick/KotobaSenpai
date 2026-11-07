namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// The overlay window handles that belong to this process. The window layer registers and
/// unregisters its HWNDs, and the Win32 event tracker consults the snapshot so that events raised
/// by our own overlay never look like target-window activity. Owned by the platform hook layer,
/// consumed by <c>KotobaSenpai.Presentation.Wpf</c> through <c>InternalsVisibleTo</c>: the state is
/// a native-handle concern, not a presentation concern, so it must not live with the windows.
/// </summary>
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

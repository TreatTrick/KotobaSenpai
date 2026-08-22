namespace KotobaSenpai.Core.Models;

/// <summary>The current state of a tracked target window.</summary>
public sealed record TargetWindowSnapshot(
    nint Handle,
    string Title,
    ScreenRect Bounds,
    double DpiScale,
    bool IsVisible,
    bool IsMinimized,
    bool IsForeground,
    bool IsOccluded = false)
{
    public WindowTarget Target => new(Handle, Title, Bounds);

    public bool IsCapturable => IsVisible && !IsMinimized && !IsOccluded;

    public bool IsRenderable => IsCapturable && IsForeground;
}

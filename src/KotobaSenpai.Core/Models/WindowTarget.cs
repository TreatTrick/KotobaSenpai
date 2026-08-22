namespace KotobaSenpai.Core.Models;

/// <summary>A short-lived reference to the target window; the handle is used only within the current process and is not persisted.</summary>
public sealed record WindowTarget
{
    public WindowTarget(nint handle, string? title, ScreenRect bounds)
    {
        if (handle == nint.Zero)
            throw new ArgumentException("Window handle is invalid.", nameof(handle));

        Handle = handle;
        Title = title?.Trim() ?? string.Empty;
        Bounds = bounds;
    }

    public nint Handle { get; }

    public string Title { get; }

    public ScreenRect Bounds { get; }

    public WindowTarget WithBounds(ScreenRect bounds)
        => new(Handle, Title, bounds);
}

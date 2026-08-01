namespace KotobaSenpai.Core.Models;

/// <summary>短生命周期的目标窗口引用；句柄只在当前进程内使用，不持久化。</summary>
public sealed record WindowTarget
{
    public WindowTarget(nint handle, string? title, ScreenRect bounds)
    {
        if (handle == nint.Zero)
            throw new ArgumentException("窗口句柄无效。", nameof(handle));

        Handle = handle;
        Title = title?.Trim() ?? string.Empty;
        Bounds = bounds;
    }

    public nint Handle { get; }

    public string Title { get; }

    public ScreenRect Bounds { get; }
}

using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: captures a single frame of the target window. Pixel data is passed only within the process and is not persisted.</summary>
public interface IWindowFrameCapture
{
    /// <summary>
    /// Captures the target window. When <paramref name="region"/> (window-relative pixels) is provided, only that
    /// sub-rectangle of the screen is captured directly (no whole-window grab then crop); otherwise the whole window.
    /// </summary>
    Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null);
}

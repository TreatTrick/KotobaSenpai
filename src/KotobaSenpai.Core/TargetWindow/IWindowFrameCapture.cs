using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Core.TargetWindow;

/// <summary>Port: captures a single frame of the target window. Pixel data is passed only within the process and is not persisted.</summary>
public interface IWindowFrameCapture
{
    /// <summary>
    /// Returns owned BGRA pixels from the target client area, optionally restricted to a client-relative region.
    /// Pixels must come from the selected window independently of other windows covering it.
    /// </summary>
    Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default, ScreenRect? region = null);

    /// <summary>Cancels outstanding capture and releases the current session. A later request can start a new session.</summary>
    void Stop();
}

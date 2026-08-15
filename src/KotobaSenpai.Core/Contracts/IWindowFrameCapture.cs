using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: captures a single frame of the target window. Pixel data is passed only within the process and is not persisted.</summary>
public interface IWindowFrameCapture
{
    Task<CapturedFrame> CaptureAsync(WindowTarget target, CancellationToken cancellationToken = default);
}

using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.TargetWindow;

/// <summary>An in-memory frame from one capture; the pixel data exists only during the recognition session and is not persisted.</summary>
public sealed record CapturedFrame
{
    public CapturedFrame(int width, int height, ReadOnlyMemory<byte> bgra32, long captureGeneration = 0)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (captureGeneration < 0)
            throw new ArgumentOutOfRangeException(nameof(captureGeneration));
        if (bgra32.Length < checked(width * height * 4))
            throw new InvalidFrameException(
                ErrorCodes.FramePixelDataTooShort,
                nameof(bgra32),
                "Frame pixel data is too short.");

        Width = width;
        Height = height;
        Bgra32 = bgra32;
        CaptureGeneration = captureGeneration;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Bgra32 { get; }

    /// <summary>Identifies samples from the same capture geometry and session; zero is an unversioned frame.</summary>
    public long CaptureGeneration { get; }
}

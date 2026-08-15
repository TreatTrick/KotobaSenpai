using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Models;

/// <summary>An in-memory frame from one capture; the pixel data exists only during the recognition session and is not persisted.</summary>
public sealed record CapturedFrame
{
    public CapturedFrame(int width, int height, ReadOnlyMemory<byte> bgra32)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (bgra32.Length < checked(width * height * 4))
            throw new InvalidFrameException(
                ErrorCodes.FramePixelDataTooShort,
                nameof(bgra32),
                "Frame pixel data is too short.");

        Width = width;
        Height = height;
        Bgra32 = bgra32;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Bgra32 { get; }
}

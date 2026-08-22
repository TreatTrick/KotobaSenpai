using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Result of one recognition: capture frame dimensions, characters arranged by line (reading order), and an optional warning.</summary>
public sealed record WordRecognitionResult(
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<OcrLine> Lines,
    string? Warning = null);

/// <summary>Port: performs Japanese OCR on the target window and returns character-level coordinates grouped by line.</summary>
public interface IWindowWordRecognizer
{
    /// <summary>
    /// Recognizes the target window. When <paramref name="region"/> (window-relative pixels, in the frame's coordinate
    /// space) is provided, the frame is cropped to that region before OCR and result coordinates are offset back to the
    /// full frame; otherwise the whole window is recognized.
    /// </summary>
    Task<WordRecognitionResult> RecognizeAsync(
        Guid recognitionId,
        WindowTarget target,
        CancellationToken cancellationToken = default,
        ScreenRect? region = null);
}

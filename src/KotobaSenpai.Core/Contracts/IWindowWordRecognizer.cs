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
    Task<WordRecognitionResult> RecognizeAsync(WindowTarget target, CancellationToken cancellationToken = default);
}
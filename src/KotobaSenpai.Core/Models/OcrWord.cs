namespace KotobaSenpai.Core.Models;

/// <summary>A character returned by OCR and its physical-pixel position in the capture frame. The meikiocr engine outputs character-level granularity.</summary>
public sealed record OcrWord
{
    public OcrWord(string? text, ScreenRect frameBounds)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("OCR word must not be empty.", nameof(text));

        Text = text.Trim();
        FrameBounds = frameBounds;
    }

    public string Text { get; }

    public ScreenRect FrameBounds { get; }
}

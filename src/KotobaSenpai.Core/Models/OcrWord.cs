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

    public ScreenRect FrameBounds { get; }//这里的ScreenRect是当前OcrWord在整个被选中的窗口中的位置，不是屏幕边缘起始点，不是选定的选框起始点，是窗口
}

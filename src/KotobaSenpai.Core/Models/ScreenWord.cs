namespace KotobaSenpai.Core.Models;

/// <summary>已映射到屏幕坐标的 OCR 词。</summary>
public sealed record ScreenWord
{
    public ScreenWord(string text, ScreenRect bounds)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("OCR 词不能为空。", nameof(text));

        Text = text.Trim();
        Bounds = bounds;
    }

    public string Text { get; }

    public ScreenRect Bounds { get; }
}

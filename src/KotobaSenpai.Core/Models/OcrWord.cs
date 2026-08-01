namespace KotobaSenpai.Core.Models;

/// <summary>OCR 返回的词及其在捕获帧中的物理像素位置。</summary>
public sealed record OcrWord
{
    public OcrWord(string? text, ScreenRect frameBounds)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("OCR 词不能为空。", nameof(text));

        Text = text.Trim();
        FrameBounds = frameBounds;
    }

    public string Text { get; }

    public ScreenRect FrameBounds { get; }
}

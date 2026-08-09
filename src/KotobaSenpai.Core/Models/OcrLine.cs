namespace KotobaSenpai.Core.Models;

/// <summary>一行 OCR 识别结果：按阅读顺序排列的字符及其字符框。</summary>
public sealed record OcrLine
{
    public OcrLine(IReadOnlyList<OcrWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Words = words.ToArray();
    }

    public IReadOnlyList<OcrWord> Words { get; }

    /// <summary>按阅读顺序拼接的字符文本，用作分词输入。</summary>
    public string Text => string.Concat(Words.Select(word => word.Text));
}
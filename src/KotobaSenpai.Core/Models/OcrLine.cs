namespace KotobaSenpai.Core.Models;

/// <summary>One line of OCR recognition result: characters in reading order and their character boxes.</summary>
public sealed record OcrLine
{
    public OcrLine(IReadOnlyList<OcrWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Words = words.ToArray();
    }

    public IReadOnlyList<OcrWord> Words { get; }

    /// <summary>Character text concatenated in reading order, used as tokenizer input.</summary>
    public string Text => string.Concat(Words.Select(word => word.Text));
}
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.WordGrouping;

/// <summary>Port: regroups the line-grouped OCR characters into "words" via the tokenizer, with one union bounding box per word.</summary>
public interface IOcrWordGroupingService
{
    /// <summary>Uses prepared analysis boundaries when supplied; otherwise applies natural sentence segmentation.</summary>
    IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines, IReadOnlyList<SentenceSegment>? segments = null);
}

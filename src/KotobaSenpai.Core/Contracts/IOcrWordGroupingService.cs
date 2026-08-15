using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: regroups the line-grouped OCR characters into "words" via the tokenizer, with one union bounding box per word.</summary>
public interface IOcrWordGroupingService
{
    IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines);
}
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：把按行分组的 OCR 字符经分词器重新组合成"词"，每词一个并集包围盒。</summary>
public interface IOcrWordGroupingService
{
    IReadOnlyList<GroupedWord> Group(IReadOnlyList<OcrLine> lines);
}
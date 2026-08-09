namespace KotobaSenpai.Core.Models;

/// <summary>分词器把一行 OCR 字符重新组合后的一个"词"：token 及其成员字符框的并集包围盒。</summary>
public sealed record GroupedWord(Token Token, ScreenRect Bounds);
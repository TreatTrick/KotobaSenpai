namespace KotobaSenpai.Core.Models;

/// <summary>词典的一个义项：词性标注与英文释义。</summary>
public sealed record DictionarySense(IReadOnlyList<string> Pos, IReadOnlyList<string> Glosses);
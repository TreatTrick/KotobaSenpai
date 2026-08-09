using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：依据分词 token 查词典，返回匹配的条目。</summary>
public interface IDictionaryLookup
{
    IReadOnlyList<DictionaryEntry> Lookup(Token token);
}
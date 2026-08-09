using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：按表记（汉字）或平假读音查 JMdict 条目。实现位于 Platform（SQLite）。</summary>
public interface IJmdictRepository
{
    IReadOnlyList<DictionaryEntry> FindByKanji(string kanji);

    IReadOnlyList<DictionaryEntry> FindByKana(string kana);
}
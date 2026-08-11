using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>端口：按表记（汉字）或平假读音查 JMdict 条目。实现位于 Platform（SQLite）。</summary>
public interface IJmdictRepository
{
    IReadOnlyList<DictionaryEntry> FindByKanji(string kanji);

    IReadOnlyList<DictionaryEntry> FindByKana(string kana);

    /// <summary>
    /// 按多个表记/读音键批量返回结果，键为数据库中的原始 form。
    /// 默认实现保留旧适配器兼容性；SQLite 适配器覆盖为单连接批量查询。
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> FindByForms(
        IReadOnlyCollection<string> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);
        var result = new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal);
        foreach (var form in forms.Where(form => !string.IsNullOrEmpty(form)).Distinct(StringComparer.Ordinal))
        {
            var entries = FindByKanji(form).Concat(FindByKana(form)).ToArray();
            if (entries.Length > 0)
                result[form] = entries;
        }
        return result;
    }
}

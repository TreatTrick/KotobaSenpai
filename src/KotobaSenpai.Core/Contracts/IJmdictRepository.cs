using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Contracts;

/// <summary>Port: looks up JMdict entries by orthography (kanji) or kana reading. Implementations live in Platform (SQLite).</summary>
public interface IJmdictRepository
{
    IReadOnlyList<DictionaryEntry> FindByKanji(string kanji);

    IReadOnlyList<DictionaryEntry> FindByKana(string kana);

    /// <summary>
    /// Returns results batched by multiple orthography/reading keys, the keys being the raw forms in the
    /// database. The default implementation preserves legacy adapter compatibility; the SQLite adapter overrides
    /// it with a single-connection batch query.
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

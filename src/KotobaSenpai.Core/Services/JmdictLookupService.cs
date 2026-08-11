using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 依据 token 的 lemma（辞书形）查词典，按 Lemma → OrthBase → Reading → BaseReading 回退。
/// 每个键先查表记表再查读音表（归一为平假名）；全未命中返回空。
/// MeCab 已给出辞书形，无需去活用引擎。
/// </summary>
public sealed class JmdictLookupService : IDictionaryLookup, IBatchDictionaryLookup
{
    private readonly IJmdictRepository _repository;

    public JmdictLookupService(IJmdictRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public IReadOnlyList<DictionaryEntry> Lookup(Token token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var forms = TokenLookupKeys(token).ToArray();
        var matches = LookupForms(forms);
        foreach (var form in forms)
        {
            if (matches.TryGetValue(form, out var result))
                return result;
        }

        return Array.Empty<DictionaryEntry>();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> LookupForms(
        IReadOnlyCollection<string> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);

        var requested = forms
            .Where(form => !string.IsNullOrEmpty(form))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0)
            return new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal);

        var queryForms = requested
            .SelectMany(form => new[] { form, Kana.ToHiragana(form) })
            .Where(form => !string.IsNullOrEmpty(form))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var raw = _repository.FindByForms(queryForms);
        var result = new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal);
        foreach (var form in requested)
        {
            if (raw.TryGetValue(form, out var entries) && entries.Count > 0)
            {
                result[form] = entries;
                continue;
            }

            var normalized = Kana.ToHiragana(form);
            if (raw.TryGetValue(normalized, out entries) && entries.Count > 0)
                result[form] = entries;
        }
        return result;
    }

    private static IEnumerable<string> TokenLookupKeys(Token token)
    {
        var keys = new[]
        {
            token.Lemma,
            token.OrthBase,
            Kana.ToHiragana(token.Reading),
            Kana.ToHiragana(token.BaseReading),
        };
        return keys.Where(key => !string.IsNullOrEmpty(key)).Distinct(StringComparer.Ordinal);
    }
}

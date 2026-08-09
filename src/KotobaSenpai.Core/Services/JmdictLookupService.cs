using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Services;

/// <summary>
/// 依据 token 的 lemma（辞书形）查词典，按 Lemma → OrthBase → Reading → BaseReading 回退。
/// 每个键先查表记表再查读音表（归一为平假名）；全未命中返回空。
/// MeCab 已给出辞书形，无需去活用引擎。
/// </summary>
public sealed class JmdictLookupService : IDictionaryLookup
{
    private readonly IJmdictRepository _repository;

    public JmdictLookupService(IJmdictRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public IReadOnlyList<DictionaryEntry> Lookup(Token token)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (TryLookup(token.Lemma, out var result)
            || TryLookup(token.OrthBase, out result)
            || TryLookup(Kana.ToHiragana(token.Reading), out result)
            || TryLookup(Kana.ToHiragana(token.BaseReading), out result))
            return result;

        return Array.Empty<DictionaryEntry>();
    }

    /// <summary>对单个键先查表记表、再查读音表；命中返回 true。</summary>
    private bool TryLookup(string key, out IReadOnlyList<DictionaryEntry> result)
    {
        if (string.IsNullOrEmpty(key))
        {
            result = Array.Empty<DictionaryEntry>();
            return false;
        }

        var byKanji = _repository.FindByKanji(key);
        if (byKanji.Count > 0)
        {
            result = byKanji;
            return true;
        }

        var byKana = _repository.FindByKana(key);
        result = byKana;
        return byKana.Count > 0;
    }
}
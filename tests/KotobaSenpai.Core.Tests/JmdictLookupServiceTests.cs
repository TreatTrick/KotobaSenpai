using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

public sealed class JmdictLookupServiceTests
{
    [Fact]
    public void Lookup_by_lemma_returns_entry()
    {
        var repo = new FakeRepo();
        repo.AddKanji("受ける", Entry("受ける", "うける", "to receive"));
        var service = new JmdictLookupService(repo);

        var result = service.Lookup(Token("受ける", lemma: "受ける", reading: ""));

        Assert.Single(result);
        Assert.Equal("受ける", result[0].Headword);
    }

    [Fact]
    public void Lookup_falls_back_to_reading_with_katakana_normalization()
    {
        var repo = new FakeRepo();
        repo.AddKana("うける", Entry("受ける", "うける", "to receive"));
        var service = new JmdictLookupService(repo);

        // lemma 未命中，reading 为片假名 → 归一为平假名 'うける' 命中读音表。
        var result = service.Lookup(Token("受ける", lemma: "受ケル", reading: "ウケル"));

        Assert.Single(result);
        Assert.Equal("受ける", result[0].Headword);
    }

    [Fact]
    public void Lookup_no_match_returns_empty()
    {
        var service = new JmdictLookupService(new FakeRepo());

        var result = service.Lookup(Token("存在しない", lemma: "存在しない", reading: "ソンザイシナイ"));

        Assert.Empty(result);
    }

    [Fact]
    public void Lookup_returns_entry_with_multiple_senses()
    {
        var repo = new FakeRepo();
        repo.AddKanji("受ける", new DictionaryEntry("受ける", "うける",
            [new DictionarySense(["動詞"], ["to receive"]), new DictionarySense(["動詞"], ["to undergo"])]));
        var service = new JmdictLookupService(repo);

        var result = service.Lookup(Token("受ける", lemma: "受ける", reading: ""));

        var entry = Assert.Single(result);
        Assert.Equal(2, entry.Senses.Count);
        Assert.Equal("to receive", entry.Senses[0].Glosses[0]);
    }

    [Fact]
    public void Lookup_forms_uses_batch_query_and_maps_hiragana_fallback_to_original_key()
    {
        var repo = new FakeRepo { ThrowOnSingleLookup = true };
        repo.AddKana("でも", Entry("でも", "でも", "but"));
        var service = new JmdictLookupService(repo);

        var result = service.LookupForms(["デモ"]);

        var entry = Assert.Single(result["デモ"]);
        Assert.Equal("でも", entry.Headword);
    }

    private static Token Token(string surface, string lemma, string reading)
        => new(surface, lemma, lemma, reading, reading, reading,
            new PartsOfSpeech("", "", "", ""), "", "", "", 0);

    private static DictionaryEntry Entry(string headword, string reading, string gloss)
        => new(headword, reading, [new DictionarySense(["動詞"], [gloss])]);

    private sealed class FakeRepo : IJmdictRepository
    {
        private readonly Dictionary<string, List<DictionaryEntry>> _kan = new();
        private readonly Dictionary<string, List<DictionaryEntry>> _kana = new();

        public bool ThrowOnSingleLookup { get; init; }

        public void AddKanji(string key, params DictionaryEntry[] entries) => _kan[key] = entries.ToList();
        public void AddKana(string key, params DictionaryEntry[] entries) => _kana[key] = entries.ToList();

        public IReadOnlyList<DictionaryEntry> FindByKanji(string kanji)
            => ThrowOnSingleLookup
                ? throw new InvalidOperationException("single-form lookup used")
                : _kan.TryGetValue(kanji, out var v) ? v : [];

        public IReadOnlyList<DictionaryEntry> FindByKana(string kana)
            => ThrowOnSingleLookup
                ? throw new InvalidOperationException("single-form lookup used")
                : _kana.TryGetValue(kana, out var v) ? v : [];

        public IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> FindByForms(
            IReadOnlyCollection<string> forms)
        {
            var result = new Dictionary<string, IReadOnlyList<DictionaryEntry>>(StringComparer.Ordinal);
            foreach (var form in forms.Distinct(StringComparer.Ordinal))
            {
                var entries = new List<DictionaryEntry>();
                if (_kan.TryGetValue(form, out var kanji))
                    entries.AddRange(kanji);
                if (_kana.TryGetValue(form, out var kana))
                    entries.AddRange(kana.Where(entry => !entries.Contains(entry)));
                if (entries.Count > 0)
                    result[form] = entries;
            }
            return result;
        }
    }
}

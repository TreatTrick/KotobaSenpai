using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Core.Tests;

public sealed class TokenSpanResolverTests
{
    [Fact]
    public void Resolves_longest_surface_without_crossing_token_boundaries()
    {
        var lookup = new FakeLookup(
            Entry("で"), Entry("も"), Entry("でも"), Entry("ちゃんと"), Entry("もち"));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([
            Token("で", 0, "デ", pos1: "助詞"),
            Token("も", 1, "モ", pos1: "助詞"),
            Token("ちゃんと", 2, "チャント", pos1: "副詞"),
        ]);

        Assert.Equal(new[] { "でも", "ちゃんと" }, spans.Select(span => span.Surface));
        Assert.DoesNotContain(spans, span => span.Surface == "もち");
        Assert.DoesNotContain("もち", lookup.RequestedForms);
        Assert.Equal("でも", spans[0].LookupKey);
    }

    [Fact]
    public void Resolves_longest_surface_across_multiple_complete_tokens()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(
            Entry("そ"), Entry("し"), Entry("たら"), Entry("そしたら")));

        var spans = resolver.Resolve([
            Token("そ", 0, "ソ", pos1: "副詞"),
            Token("し", 1, "シ", lemma: "為る", pos1: "動詞"),
            Token("たら", 2, "タラ", lemma: "た", pos1: "助動詞"),
        ]);

        var span = Assert.Single(spans);
        Assert.Equal("そしたら", span.Surface);
        Assert.Equal("そしたら", span.LookupKey);
    }

    [Fact]
    public void Extends_inflected_adjective_through_auxiliary_and_uses_base_lemma()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(Entry("無い")));

        var spans = resolver.Resolve([
            Token("なかっ", 0, "ナカッ", lemma: "無い", pos1: "形容詞"),
            Token("た", 3, "タ", lemma: "た", pos1: "助動詞"),
        ]);

        var span = Assert.Single(spans);
        Assert.Equal("なかった", span.Surface);
        Assert.Equal("無い", span.LookupKey);
        Assert.Single(span.Entries);
    }

    [Fact]
    public void Extends_inflected_verb_through_auxiliary()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(Entry("考える")));

        var spans = resolver.Resolve([
            Token("考え", 0, "カンガエ", lemma: "考える", pos1: "動詞"),
            Token("てる", 2, "テル", lemma: "てる", pos1: "助動詞"),
        ]);

        var span = Assert.Single(spans);
        Assert.Equal("考えてる", span.Surface);
        Assert.Equal("考える", span.LookupKey);
    }

    [Fact]
    public void Prefers_unidic_lemma_over_homographic_surface_for_an_inflection_chain()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(
            Entry("した"), Entry("為る")));

        var spans = resolver.Resolve([
            Token("し", 0, "シ", lemma: "為る", pos1: "動詞"),
            Token("た", 1, "タ", lemma: "た", pos1: "助動詞"),
        ]);

        var span = Assert.Single(spans);
        Assert.Equal("した", span.Surface);
        Assert.Equal("為る", span.LookupKey);
        Assert.Equal("為る", Assert.Single(span.Entries).Headword);
    }

    [Fact]
    public void Punctuation_is_a_hard_boundary_and_unmatched_tokens_are_retained()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(Entry("でも"), Entry("ちゃんと")));

        var spans = resolver.Resolve([
            Token("で", 0, "デ", pos1: "助詞"),
            Token("も", 1, "モ", pos1: "助詞"),
            Token("、", 2, "、", lemma: "、", pos1: "補助記号", pos2: "読点"),
            Token("未知", 3, "ミチ", lemma: "未知", pos1: "名詞"),
        ]);

        Assert.Equal(new[] { "でも", "未知" }, spans.Select(span => span.Surface));
        Assert.Empty(spans[1].Entries);
    }

    [Fact]
    public void Does_not_match_a_dictionary_form_across_punctuation()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(
            Entry("で"), Entry("も"), Entry("でも")));

        var spans = resolver.Resolve([
            Token("で", 0, "デ", pos1: "助詞"),
            Token("、", 1, "、", lemma: "、", pos1: "補助記号", pos2: "読点"),
            Token("も", 2, "モ", pos1: "助詞"),
        ]);

        Assert.Equal(new[] { "で", "も" }, spans.Select(span => span.Surface));
    }

    [Fact]
    public void Does_not_match_a_dictionary_form_across_an_offset_gap()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(
            Entry("も"), Entry("ちゃんと"), Entry("もちゃんと")));

        var spans = resolver.Resolve([
            Token("も", 0, "モ", pos1: "助詞"),
            Token("ちゃんと", 2, "チャント", pos1: "副詞"),
        ]);

        Assert.Equal(new[] { "も", "ちゃんと" }, spans.Select(span => span.Surface));
    }

    [Fact]
    public void Resolves_all_segments_with_one_batch_lookup()
    {
        var lookup = new FakeLookup(Entry("でも"), Entry("そしたら")) { MaxCalls = 1 };
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([
            Token("で", 0, "デ", pos1: "助詞"),
            Token("も", 1, "モ", pos1: "助詞"),
            Token("。", 2, "。"),
            Token("そ", 3, "ソ", pos1: "副詞"),
            Token("し", 4, "シ", lemma: "為る", pos1: "動詞"),
            Token("たら", 5, "タラ", lemma: "た", pos1: "助動詞"),
        ]);

        Assert.Equal(new[] { "でも", "そしたら" }, spans.Select(span => span.Surface));
    }

    [Fact]
    public void Lookup_span_preserves_source_tokens_and_derived_offsets()
    {
        var first = Token("で", 4, "デ");
        var second = Token("も", 5, "モ");
        var entries = new[]
        {
            new DictionaryEntry("でも", "でも", [
                new DictionarySense(["接続詞"], ["but"])
            ])
        };

        var span = new LookupSpan([first, second], "でも", entries);

        Assert.Equal("でも", span.Surface);
        Assert.Equal("デモ", span.Reading);
        Assert.Equal("でも", span.LookupKey);
        Assert.Equal(4, span.StartOffset);
        Assert.Equal(6, span.EndOffset);
        Assert.Equal(new[] { first, second }, span.Tokens);
        Assert.Same(entries[0], Assert.Single(span.Entries));
    }

    [Fact]
    public void Grouped_word_exposes_the_resolved_span_entries()
    {
        var token = Token("でも", 0, "デモ");
        var entry = new DictionaryEntry("でも", "でも", []);
        var word = new GroupedWord(
            new LookupSpan([token], "でも", [entry]),
            new ScreenRect(10, 20, 30, 12));

        Assert.Equal("でも", word.Surface);
        Assert.Equal("でも", word.LookupKey);
        Assert.Same(entry, Assert.Single(word.Entries));
        Assert.True(word.HasResolvedLookup);
        Assert.Equal(new ScreenRect(10, 20, 30, 12), word.Bounds);
    }

    [Fact]
    public void Legacy_grouped_words_keep_original_value_equality()
    {
        var token = Token("でも", 0, "デモ");
        var bounds = new ScreenRect(10, 20, 30, 12);

        Assert.Equal(new GroupedWord(token, bounds), new GroupedWord(token, bounds));
        Assert.False(new GroupedWord(token, bounds).HasResolvedLookup);
    }

    private static Token Token(
        string surface,
        int start,
        string reading,
        string? lemma = null,
        string pos1 = "",
        string pos2 = "")
        => new(surface, lemma ?? surface, lemma ?? surface, reading, reading, reading,
            new PartsOfSpeech(pos1, pos2, "", ""), "", "", "", start);

    private static DictionaryEntry Entry(string form)
        => new(form, form, [new DictionarySense([], [$"meaning:{form}"])]);

    private sealed class FakeLookup : IBatchDictionaryLookup
    {
        private readonly Dictionary<string, IReadOnlyList<DictionaryEntry>> _entries;

        public HashSet<string> RequestedForms { get; } = new(StringComparer.Ordinal);

        public int MaxCalls { get; init; } = int.MaxValue;

        private int Calls { get; set; }

        public FakeLookup(params DictionaryEntry[] entries)
        {
            _entries = entries.ToDictionary(entry => entry.Headword,
                entry => (IReadOnlyList<DictionaryEntry>)[entry], StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> LookupForms(
            IReadOnlyCollection<string> forms)
        {
            Calls++;
            if (Calls > MaxCalls)
                throw new InvalidOperationException("candidate lookup was not batched across OCR lines");
            RequestedForms.UnionWith(forms);
            return forms
                .Where(form => _entries.ContainsKey(form))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(form => form, form => _entries[form], StringComparer.Ordinal);
        }
    }
}

using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Geometry;

namespace KotobaSenpai.Core.Tests.Dictionary;

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

    [Theory]
    [InlineData("1", "週間", "一週間", "いっしゅうかん")]
    [InlineData("１", "週間", "一週間", "いっしゅうかん")]
    [InlineData("21", "週間", "二十一週間", "にじゅういっしゅうかん")]
    public void NormalizesNumericLookupWithoutChangingTheSurface(string number, string counter, string key, string reading)
    {
        var lookup = new FakeLookup(new DictionaryEntry(key, reading, [])) { MaxCalls = 1 };
        var resolver = new TokenBoundarySpanResolver(lookup);
        var span = Assert.Single(resolver.Resolve([Token(number, 0, ""), Token(counter, number.Length, "シュウカン")]));
        Assert.Equal(number + counter, span.Surface);
        Assert.Equal(key, span.LookupKey);
        Assert.Equal(reading, span.Reading);
        Assert.Contains(key, lookup.RequestedForms);
    }

    [Theory]
    [InlineData("三", "週間", "さんしゅうかん")]
    [InlineData("六", "ヶ月", "ろっかげつ")]
    [InlineData("3", "ヶ月", "さんかげつ")]
    [InlineData("４", "時間", "よじかん")]
    [InlineData("十九", "時間", "じゅうくじかん")]
    [InlineData("四", "年生", "よねんせい")]
    [InlineData("火", "曜日", "かようび")]
    [InlineData("何", "曜日", "なんようび")]
    public void GroupsWhitelistedCountersWithoutDictionaryEntries(string prefix, string counter, string reading)
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup(Entry(counter)));
        var span = Assert.Single(resolver.Resolve([Token(prefix, 0, ""), Token(counter, prefix.Length, "")]));
        Assert.Equal(prefix + counter, span.Surface);
        Assert.Equal(reading, span.Reading);
        Assert.Empty(span.Entries);
        Assert.True(span.HasOverriddenReading);
    }

    [Theory]
    [InlineData("百", "一", "度", "いちど")]
    [InlineData("三十", "八", "度", "はちど")]
    [InlineData("一", "十", "分", "じゅうぶん")]
    public void MatchesExactDictionaryCompoundsAtNumericContinuations(
        string prefix, string start, string end, string reading)
    {
        var key = start + end;
        var entry = new DictionaryEntry(key, reading, []);
        var lookup = new FakeLookup(entry);
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([Token(prefix, 0, ""), Token(start, prefix.Length, ""),
            Token(end, prefix.Length + start.Length, "")]);

        Assert.Contains(key, lookup.RequestedForms);
        Assert.Equal(new[] { prefix, key }, spans.Select(span => span.Surface));
        Assert.Same(entry, Assert.Single(spans[1].Entries));
        Assert.Equal(reading, spans[1].Reading);
    }

    [Fact]
    public void DoesNotNormalizeACompoundStartingAtTheFractionalPart()
    {
        var lookup = new FakeLookup(new DictionaryEntry("五週間", "ごしゅうかん", []));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([Token("1", 0, ""), Token(".", 1, ""),
            Token("5", 2, ""), Token("週間", 3, "")]);

        Assert.DoesNotContain("五週間", lookup.RequestedForms);
        Assert.Equal(new[] { "1", "5", "週間" }, spans.Select(span => span.Surface));
    }

    [Fact]
    public void DoesNotMergeUnsupportedNumbersOrAcrossBoundaries()
    {
        var resolver = new TokenBoundarySpanResolver(new FakeLookup());
        Assert.Equal(2, resolver.Resolve([Token("三", 0, ""), Token("冊", 1, "")]).Count);
        Assert.Equal(2, resolver.Resolve([Token("100", 0, ""), Token("週間", 3, "")]).Count);
        Assert.Equal(3, resolver.Resolve([Token("10", 0, ""), Token("1", 2, ""), Token("週間", 3, "")]).Count);
        Assert.Equal(3, resolver.Resolve([Token("百", 0, ""), Token("三", 1, ""), Token("週間", 2, "")]).Count);
        Assert.Equal(3, resolver.Resolve([Token("1", 0, ""), Token(".", 1, ""), Token("5", 2, ""), Token("週間", 3, "")]).Count);
        Assert.Equal(2, resolver.Resolve([Token("三", 0, ""), Token("週間", 2, "")]).Count);
    }

    [Theory]
    [InlineData("01")]
    [InlineData("０１")]
    [InlineData("0１")]
    public void PreservesLeadingZerosWithoutInferringACounterReading(string number)
    {
        var lookup = new FakeLookup(new DictionaryEntry("一週間", "いっしゅうかん", []));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([Token(number, 0, ""), Token("週間", number.Length, "シュウカン")]);

        Assert.Equal(new[] { number, "週間" }, spans.Select(span => span.Surface));
        Assert.DoesNotContain("一週間", lookup.RequestedForms);
    }

    [Fact]
    public void Does_not_end_compound_on_prefix_preventing_particle_plus_prefix_collision()
    {
        var lookup = new FakeLookup(Entry("と"), Entry("とお"), Entry("お米"));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([
            Token("と", 0, "ト", pos1: "助詞", pos2: "格助詞"),
            Token("お", 1, "オ", pos1: "接頭辞"),
            Token("米", 2, "コメ", pos1: "名詞", pos2: "普通名詞"),
        ]);

        Assert.Equal(new[] { "と", "お米" }, spans.Select(span => span.Surface));
        Assert.DoesNotContain("とお", lookup.RequestedForms);
    }

    [Fact]
    public void Still_matches_compounds_spanning_multiple_leading_prefixes()
    {
        var lookup = new FakeLookup(Entry("ご無沙汰"));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([
            Token("ご", 0, "ゴ", pos1: "接頭辞"),
            Token("無", 1, "ブ", pos1: "接頭辞"),
            Token("沙汰", 2, "サタ", pos1: "名詞"),
        ]);

        var span = Assert.Single(spans);
        Assert.Equal("ご無沙汰", span.Surface);
        Assert.Equal("ご無沙汰", span.LookupKey);
    }

    [Fact]
    public void Groups_renyoukei_verb_with_adjectival_suffix_and_auxiliaries_when_absent_from_dictionary()
    {
        var lookup = new FakeLookup(Entry("食べる"));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([
            Token("食べ", 0, "タベ", lemma: "食べる", pos1: "動詞", pos2: "一般", cForm: "連用形-一般"),
            Token("やすかっ", 2, "ヤスカッ", lemma: "易い", orthBase: "やすい", pos1: "接尾辞", pos2: "形容詞的", cForm: "連用形-促音便"),
            Token("た", 6, "タ", lemma: "た", pos1: "助動詞"),
        ]);

        var span = Assert.Single(spans);
        Assert.Equal("食べやすかった", span.Surface);
        Assert.Equal("食べる", span.LookupKey);
        Assert.Equal(3, span.Tokens.Count);
    }

    [Fact]
    public void Prefers_dictionary_compound_over_verb_plus_adjectival_suffix_fallback()
    {
        var lookup = new FakeLookup(Entry("分かる"), new DictionaryEntry("分かりやすい", "わかりやすい", []));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var exact = Assert.Single(resolver.Resolve([
            Token("分かり", 0, "ワカリ", lemma: "分かる", pos1: "動詞", pos2: "一般", cForm: "連用形-一般"),
            Token("やすい", 3, "ヤスイ", lemma: "易い", orthBase: "やすい", pos1: "接尾辞", pos2: "形容詞的", cForm: "終止形-一般"),
        ]));
        Assert.Equal("分かりやすい", exact.Surface);
        Assert.Equal("分かりやすい", exact.LookupKey);

        var inflected = Assert.Single(resolver.Resolve([
            Token("分かり", 0, "ワカリ", lemma: "分かる", pos1: "動詞", pos2: "一般", cForm: "連用形-一般"),
            Token("やすかっ", 3, "ヤスカッ", lemma: "易い", orthBase: "やすい", baseReading: "ヤスイ", pos1: "接尾辞", pos2: "形容詞的", cForm: "連用形-促音便"),
            Token("た", 7, "タ", lemma: "た", pos1: "助動詞"),
        ]));
        Assert.Equal("分かりやすかった", inflected.Surface);
        Assert.Equal("分かりやすい", inflected.LookupKey);
        Assert.Equal(3, inflected.Tokens.Count);
    }

    [Fact]
    public void Does_not_attach_adjectival_suffix_to_non_renyoukei_verb()
    {
        var lookup = new FakeLookup(Entry("食べる"));
        var resolver = new TokenBoundarySpanResolver(lookup);

        var spans = resolver.Resolve([
            Token("食べる", 0, "タベル", lemma: "食べる", pos1: "動詞", pos2: "一般", cForm: "終止形-一般"),
            Token("やすい", 3, "ヤスイ", lemma: "易い", orthBase: "やすい", pos1: "接尾辞", pos2: "形容詞的", cForm: "終止形-一般"),
        ]);

        Assert.Equal(new[] { "食べる", "やすい" }, spans.Select(span => span.Surface));
    }

    private static Token Token(
        string surface,
        int start,
        string reading,
        string? lemma = null,
        string pos1 = "",
        string pos2 = "",
        string? orthBase = null,
        string? baseReading = null,
        string cForm = "")
        => new(surface, lemma ?? surface, orthBase ?? lemma ?? surface, reading, baseReading ?? reading, reading,
            new PartsOfSpeech(pos1, pos2, "", ""), "", cForm, "", start);

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

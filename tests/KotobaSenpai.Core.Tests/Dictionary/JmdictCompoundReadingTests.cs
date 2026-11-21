using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Tokenization;

namespace KotobaSenpai.Core.Tests.Dictionary;

public sealed class JmdictCompoundReadingTests
{
    [Theory]
    [InlineData("頭", "アタマ", "垂れる", "タレル", "こうべをたれる")]
    [InlineData("踵", "カカト", "返す", "カエス", "きびすをかえす")]
    public void Uses_unique_dictionary_reading_for_exact_idiom(
        string noun, string nounReading, string verb, string verbReading, string dictionaryReading)
    {
        var surface = noun + "を" + verb;
        var lookup = new FakeLookup(surface, dictionaryReading);
        var tokens = new[] { Token(noun, 0, nounReading), Token("を", 1, "ヲ"), Token(verb, 2, verbReading) };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        Assert.Equal(surface, span.LookupKey);
        Assert.Equal(tokens, span.Tokens);
        AssertOverride(span, dictionaryReading);
    }

    [Theory]
    [InlineData("頭", "アタマ", "垂れ", "垂れる", "タレ", "タレル", "た", "助動詞", "こうべをたれる", "こうべをたれた")]
    [InlineData("踵", "カカト", "返し", "返す", "カエシ", "カエス", "て", "助詞", "きびすをかえす", "きびすをかえして")]
    [InlineData("袖", "ソデ", "引い", "引く", "ヒイ", "ヒク", "て", "助詞", "そでをひく", "ソデヲヒイテ")]
    [InlineData("踵", "カカト", "返し", "返す", "カエシ", "カエス", "て", "助詞", "きびすをもどす", "カカトヲカエシテ")]
    public void Matches_inflected_idiom_and_only_overrides_when_the_reading_aligns_and_differs(
        string noun, string nounReading, string verb, string baseForm, string verbReading, string baseReading,
        string tail, string tailPartOfSpeech, string dictionaryReading, string expectedReading)
    {
        var key = noun + "を" + baseForm;
        var lookup = new FakeLookup(key, dictionaryReading);
        var tokens = new[]
        {
            Token(noun, 0, nounReading), Token("を", 1, "ヲ"),
            Token(verb, 2, verbReading, lemma: verb, orthBase: baseForm, baseReading: baseReading, pos1: "動詞"),
            Token(tail, 2 + verb.Length, tail == "て" ? "テ" : "タ", pos1: tailPartOfSpeech, pos2: "接続助詞"),
        };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        Assert.Equal(noun + "を" + verb + tail, span.Surface);
        Assert.Equal(tokens, span.Tokens);
        Assert.Equal(key, span.LookupKey);
        Assert.Contains(key, lookup.RequestedForms);
        if (expectedReading == string.Concat(tokens.Select(token => token.Reading)))
            AssertOriginal(span, expectedReading);
        else
            AssertOverride(span, expectedReading);
    }

    [Fact]
    public void Finds_an_inflected_adjective_by_lemma_and_absorbs_a_chain()
    {
        var lookup = new FakeLookup("間が悪い", "まがわるい");
        var tokens = new[]
        {
            Token("間", 0, "アイダ"), Token("が", 1, "ガ"),
            Token("悪く", 2, "ワルク", lemma: "悪い", orthBase: "わるい", baseReading: "ワルイ", pos1: "形容詞"),
            Token("なかっ", 4, "ナカッ", pos1: "助動詞"), Token("た", 7, "タ", pos1: "助動詞"),
        };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        Assert.Equal("間が悪くなかった", span.Surface);
        Assert.Equal("間が悪い", span.LookupKey);
        AssertOverride(span, "まがわるくなかった");
    }

    [Fact]
    public void Normalizes_and_deduplicates_dictionary_readings_for_one_week()
    {
        var lookup = new FakeLookup("一週間", "いっしゅうかん", "イッシュウカン", "");
        var tokens = new[] { Token("一", 0, "イチ"), Token("週間", 1, "シュウカン") };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        AssertOverride(span, "いっしゅうかん");
    }

    [Fact]
    public void Preserves_the_existing_single_token_reading_for_man_atari()
    {
        var lookup = new FakeLookup("目の当たり", "まのあたり");
        var tokens = new[] { Token("目の当たり", 0, "マノアタリ") };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        AssertOriginal(span, "マノアタリ");
        Assert.Single(span.Entries);
    }

    [Theory]
    [InlineData("一週間", "いっしゅうかん", "ひとしゅうかん")]
    [InlineData("一週間", "いちしゅうかん", "")]
    [InlineData("一週間", "invalid reading", "")]
    [InlineData("一週間", "", "")]
    public void Keeps_original_reading_when_a_compound_reading_is_ambiguous_equivalent_or_invalid(
        string surface, string firstReading, string secondReading)
    {
        var lookup = new FakeLookup(surface, firstReading, secondReading);
        var tokens = new[] { Token("一", 0, "イチ"), Token("週間", 1, "シュウカン") };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        AssertOriginal(span, "イチシュウカン");
    }

    [Fact]
    public void Keeps_original_reading_for_ambiguous_single_token()
    {
        var lookup = new FakeLookup("額", "ひたい", "がく");
        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve([Token("額", 0, "ガク")]));

        AssertOriginal(span, "ガク");
        Assert.Equal(2, span.Entries.Count);
    }

    [Fact]
    public void Keeps_original_reading_for_a_single_verb_and_its_auxiliary()
    {
        var lookup = new FakeLookup("行く", "ゆく");
        var tokens = new[]
        {
            Token("行っ", 0, "イッ", lemma: "行く", baseReading: "イク", pos1: "動詞"),
            Token("た", 2, "タ", pos1: "助動詞"),
        };

        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        Assert.Equal("行った", span.Surface);
        AssertOriginal(span, "イッタ");
    }

    [Fact]
    public void Does_not_normalize_a_kana_only_compound()
    {
        var lookup = new FakeLookup("にする", "にする");
        var tokens = new[]
        {
            Token("に", 0, "ニ", pos1: "助詞"),
            Token("し", 1, "シ", lemma: "為る", orthBase: "する", baseReading: "スル", pos1: "動詞"),
            Token("た", 2, "タ", pos1: "助動詞"),
        };

        var spans = new TokenBoundarySpanResolver(lookup).Resolve(tokens);

        Assert.Equal(new[] { "に", "し", "た" }, spans.Select(span => span.Surface));
        Assert.DoesNotContain("にする", lookup.RequestedForms);
        Assert.DoesNotContain("に為る", lookup.RequestedForms);
    }

    [Fact]
    public void Does_not_override_a_kana_only_exact_match()
    {
        var lookup = new FakeLookup("でも", "だが");
        var tokens = new[] { Token("で", 0, "デ"), Token("も", 1, "モ") };
        var span = Assert.Single(new TokenBoundarySpanResolver(lookup).Resolve(tokens));

        AssertOriginal(span, "デモ");
    }

    [Fact]
    public void Lookup_span_override_preserves_source_accent_but_clears_display_accent()
    {
        var source = Token("頭", 4, "アタマ");
        var particle = Token("を", 5, "ヲ");

        var span = new LookupSpan([source, particle], "頭を", [], overriddenReading: "こうべを");

        AssertOverride(span, "こうべを");
        Assert.Same(source, span.Tokens[0]);
        Assert.Equal(1, span.Tokens[0].PitchAccentPosition);
        Assert.Equal(4, span.StartOffset);
        Assert.Equal(6, span.EndOffset);
    }

    private static void AssertOverride(LookupSpan span, string reading)
    {
        Assert.Equal(reading, span.Reading);
        Assert.Equal(reading, span.Token.Reading);
        Assert.True(span.HasOverriddenReading);
        Assert.Empty(span.PitchAccents);
        Assert.Null(span.Token.PitchAccentPosition);
    }

    private static void AssertOriginal(LookupSpan span, string reading)
    {
        Assert.Equal(reading, span.Reading);
        Assert.False(span.HasOverriddenReading);
        Assert.Equal(span.Tokens.Count, span.PitchAccents.Count);
        Assert.Equal(1, span.Token.PitchAccentPosition);
    }

    private static Token Token(string surface, int start, string reading, string? lemma = null, string? orthBase = null,
        string? baseReading = null, string pos1 = "", string pos2 = "")
        => new(surface, lemma ?? surface, orthBase ?? lemma ?? surface, reading, baseReading ?? reading, reading,
            new PartsOfSpeech(pos1, pos2, "", ""), "", "", "1", start);

    private sealed class FakeLookup(string key, params string[] readings) : IBatchDictionaryLookup
    {
        public IReadOnlyCollection<string> RequestedForms { get; private set; } = [];

        public IReadOnlyDictionary<string, IReadOnlyList<DictionaryEntry>> LookupForms(IReadOnlyCollection<string> forms)
        {
            Assert.Empty(RequestedForms);
            RequestedForms = forms.ToArray();
            return forms.Contains(key)
                ? new Dictionary<string, IReadOnlyList<DictionaryEntry>>
                {
                    [key] = readings.Select(reading => new DictionaryEntry(key, reading, [])).ToArray(),
                }
                : new Dictionary<string, IReadOnlyList<DictionaryEntry>>();
        }
    }
}

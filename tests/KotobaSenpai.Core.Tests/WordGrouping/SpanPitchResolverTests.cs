using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Core.Tests.WordGrouping;

public sealed class SpanPitchResolverTests
{
    [Theory]
    [InlineData("一週間", "いっしゅうかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("1週間", "いっしゅうかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("１週間", "いっしゅうかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("一時間", "いちじかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("二週間", "にしゅうかん", 2, SpanPitchSource.CounterRule)]
    [InlineData("三週間", "さんしゅうかん", 3, SpanPitchSource.CounterRule)]
    [InlineData("四時間", "よじかん", 2, SpanPitchSource.CounterRule)]
    [InlineData("三年生", "さんねんせい", 3, SpanPitchSource.CounterRule)]
    [InlineData("三ヶ月", "さんかげつ", 3, SpanPitchSource.CounterRule)]
    [InlineData("火曜日", "かようび", 2, SpanPitchSource.CounterRule)]
    public void ResolvesDictionaryBeforeCounterRules(string surface, string reading, int position, SpanPitchSource source)
    {
        var dictionary = new AccentDictionary
        {
            Entries = { [("一週間", "いっしゅうかん")] = [3], [("一時間", "いちじかん")] = [3] },
        };
        var pitch = Assert.Single(new SpanPitchResolver(dictionary).Resolve([Word(surface, reading)]));
        Assert.NotNull(pitch);
        Assert.Equal(source, pitch.Source);
        Assert.Equal([position], pitch.Positions);
    }

    [Fact]
    public void UsesExactReadingAndNeverQueriesDictionaryForm()
    {
        var dictionary = new AccentDictionary
        {
            Entries = { [("額", "ひたい")] = [0], [("食べる", "たべて")] = [2], [("頭", "こうべ")] = [0, 3] },
        };
        var resolver = new SpanPitchResolver(dictionary);
        var words = new[] { Word("額", "がく").WithOverriddenReading("ひたい"), Word("食べて", "たべて", "食べる"),
            Word("三冊", "さんさつ"), Word("頭", "あたま"), Word("頭", "こうべ"), Word("三週間", "みっか") };
        var results = resolver.Resolve(words).ToArray();
        Assert.Equal([0], results[0]!.Positions);
        Assert.All(results[1..4], Assert.Null);
        Assert.Equal([0, 3], results[4]!.Positions);
        Assert.Null(results[5]);
        Assert.DoesNotContain(("食べる", "たべて"), dictionary.LastRequest);
        Assert.Equal(1, dictionary.CallCount);
    }

    [Fact]
    public void ManualOverrideWinsOnlyForTheExactReading()
    {
        var dictionary = new AccentDictionary
        {
            Entries = { [("額", "ひたい")] = [0], [("額", "がく")] = [0, 2] },
        };
        var overrides = new Dictionary<(string, string), IReadOnlyList<int>> { [("額", "ひたい")] = [2] };
        var results = new SpanPitchResolver(dictionary, overrides).Resolve([Word("額", "ひたい"), Word("額", "がく")]);
        Assert.Equal(SpanPitchSource.ManualOverride, results[0]!.Source);
        Assert.Equal([2], results[0]!.Positions);
        Assert.Equal(SpanPitchSource.Dictionary, results[1]!.Source);
        Assert.Equal([("額", "がく")], dictionary.LastRequest);
    }

    [Fact]
    public void PrefersOriginalManualOverrideAndFallsBackToTheNormalizedSurface()
    {
        var dictionary = new AccentDictionary { Entries = { [("1週間", "いっしゅうかん")] = [2] } };
        var overrides = new Dictionary<(string, string), IReadOnlyList<int>>
        {
            [("1週間", "いっしゅうかん")] = [1],
            [("一週間", "いっしゅうかん")] = [3],
        };
        var results = new SpanPitchResolver(dictionary, overrides)
            .Resolve([Word("1週間", "いっしゅうかん"), Word("１週間", "いっしゅうかん")]);

        Assert.All(results, pitch => Assert.Equal(SpanPitchSource.ManualOverride, pitch!.Source));
        Assert.Equal([1], results[0]!.Positions);
        Assert.Equal([3], results[1]!.Positions);
        Assert.Equal(0, dictionary.CallCount);
    }

    [Fact]
    public void FallsBackToMorphologicalRulesOnlyWhenDictionaryMissesAndReadingIsNotOverridden()
    {
        var dictionary = new AccentDictionary
        {
            Entries =
            {
                [("食べる", "たべて")] = [2],
                [("論理的", "ろんりてき")] = [0],
            },
        };
        var tabete = new GroupedWord(
            new LookupSpan(
                [
                    new Token("食べ", "食べる", "食べる", "タベ", "タベル", "タベ",
                        new("動詞", "一般", "", ""), "下一段-バ行", "連用形-一般", "2", 0, "C1", "M4@1"),
                    new Token("て", "て", "て", "テ", "テ", "テ",
                        new("助詞", "接続助詞", "", ""), "*", "*", "*", 2, "動詞%F1,形容詞%F2@-1", "*"),
                ],
                "食べる",
                []),
            new ScreenRect(0, 0, 30, 10));
        var overriddenTabete = tabete.WithOverriddenReading("たべて");
        var ronriteki = new GroupedWord(
            new LookupSpan(
                [
                    new Token("論理", "論理", "論理", "ロンリ", "ロンリ", "ロンリ",
                        new("名詞", "普通名詞", "", ""), "*", "*", "1", 0, "C1", "*"),
                    new Token("的", "的", "的", "テキ", "テキ", "テキ",
                        new("接尾辞", "形状詞的", "", ""), "*", "*", "*", 2, "C4", "*"),
                ],
                "論理的",
                []),
            new ScreenRect(0, 0, 30, 10));
        var tabeyou = new GroupedWord(
            new Token("食べよう", "食べる", "食べる", "タベヨウ", "タベル", "タベヨー",
                new("動詞", "一般", "", ""), "下一段-バ行", "意志推量形", "2", 0, "C1", "M1@1"),
            new ScreenRect(0, 0, 40, 10));
        var plainNoun = Word("桜", "さくら");

        var results = new SpanPitchResolver(dictionary)
            .Resolve([tabete, overriddenTabete, ronriteki, tabeyou, plainNoun]);

        Assert.Equal(SpanPitchSource.MorphologicalRule, results[0]!.Source);
        Assert.Equal([1], results[0]!.Positions);
        Assert.Null(results[1]);
        Assert.Equal(SpanPitchSource.Dictionary, results[2]!.Source);
        Assert.Equal([0], results[2]!.Positions);
        Assert.Equal(SpanPitchSource.MorphologicalRule, results[3]!.Source);
        Assert.Equal([3], results[3]!.Positions);
        Assert.Null(results[4]);
    }

    private static GroupedWord Word(string surface, string reading, string? lemma = null)
        => new(new Token(surface, lemma ?? surface, lemma ?? surface, reading, reading, reading,
            new("名詞", "", "", ""), "", "", "", 0), new ScreenRect(0, 0, 10, 10));

    private sealed class AccentDictionary : IPitchAccentDictionary
    {
        public Dictionary<(string Surface, string Reading), IReadOnlyList<int>> Entries { get; } = new();
        public IReadOnlyCollection<(string Surface, string Reading)> LastRequest { get; private set; } = [];
        public int CallCount { get; private set; }

        public IReadOnlyDictionary<(string Surface, string Reading), IReadOnlyList<int>> FindByKeys(
            IReadOnlyCollection<(string Surface, string Reading)> keys)
        {
            CallCount++;
            LastRequest = keys;
            return Entries.Where(pair => keys.Contains(pair.Key)).ToDictionary();
        }
    }
}

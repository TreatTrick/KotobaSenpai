using System.IO;
using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Ocr;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Platform.Windows.Dictionary;
using KotobaSenpai.Platform.Windows.Japanese;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>
/// Full local end-to-end pitch-accent integration tests using the real bundled UniDic 3.1.0, JMdict SQLite index,
/// and Kanjium pitch-accent SQLite index. Skips cleanly when any local offline asset is unavailable (e.g. clean CI).
/// </summary>
public sealed class PitchAccentEndToEndIntegrationTests
{
    [Theory]
    [InlineData("一週間ほど待った。", "一週間", "いっしゅうかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("1週間ほど待った。", "1週間", "いっしゅうかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("１週間ほど待った。", "１週間", "いっしゅうかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("一時間だけ話そう。", "一時間", "いちじかん", 3, SpanPitchSource.Dictionary)]
    [InlineData("21週間も続いた。", "21週間", "にじゅういっしゅうかん", 6, SpanPitchSource.CounterRule)]
    public void Dictionary_whole_words_and_numeral_normalization_resolve_end_to_end(
        string sentence, string targetSurface, string expectedReading, int expectedPrimary, SpanPitchSource expectedSource)
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var word = FindWord(pipeline.Analyze(sentence), targetSurface);
        Assert.Equal(expectedReading, Kana.ToHiragana(word.Reading));
        Assert.NotNull(word.SpanPitch);
        Assert.Equal(expectedSource, word.SpanPitch.Source);
        Assert.Equal(expectedPrimary, word.SpanPitch.Positions[0]);
    }

    [Theory]
    [InlineData("二週間と三週間。", "二週間", "にしゅうかん", 2)]
    [InlineData("二週間と三週間。", "三週間", "さんしゅうかん", 3)]
    [InlineData("四時間と九時間。", "四時間", "よじかん", 2)]
    [InlineData("四時間と九時間。", "九時間", "くじかん", 2)]
    [InlineData("三年生と四年生。", "三年生", "さんねんせい", 3)]
    [InlineData("三年生と四年生。", "四年生", "よねんせい", 2)]
    [InlineData("一ヶ月、三ヶ月、六ヶ月。", "一ヶ月", "いっかげつ", 3)]
    [InlineData("一ヶ月、三ヶ月、六ヶ月。", "三ヶ月", "さんかげつ", 3)]
    [InlineData("一ヶ月、三ヶ月、六ヶ月。", "六ヶ月", "ろっかげつ", 3)]
    [InlineData("3か月、1か月、3カ月。", "3か月", "さんかげつ", 3)]
    [InlineData("3か月、1か月、3カ月。", "1か月", "いっかげつ", 3)]
    [InlineData("3か月、1か月、3カ月。", "3カ月", "さんかげつ", 3)]
    [InlineData("火曜日と日曜日と何曜日。", "火曜日", "かようび", 2)]
    [InlineData("火曜日と日曜日と何曜日。", "日曜日", "にちようび", 3)]
    [InlineData("火曜日と日曜日と何曜日。", "何曜日", "なんようび", 3)]
    public void Whitelisted_counters_resolve_reading_and_pitch_end_to_end(
        string sentence, string targetSurface, string expectedReading, int expectedPrimary)
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var word = FindWord(pipeline.Analyze(sentence), targetSurface);
        Assert.Equal(expectedReading, Kana.ToHiragana(word.Reading));
        Assert.NotNull(word.SpanPitch);
        Assert.Equal(expectedPrimary, word.SpanPitch.Positions[0]);
    }

    [Fact]
    public void Numeric_boundaries_prevent_false_merges_while_preserving_exact_compounds()
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        Assert.DoesNotContain(pipeline.Analyze("本を三冊買った。"), word => word.Surface == "三冊");
        Assert.DoesNotContain(pipeline.Analyze("100週間も待てない。"), word => word.Surface == "100週間");
        Assert.DoesNotContain(pipeline.Analyze("1.5週間ほど休む。"), word => word.Surface == "5週間");
        Assert.DoesNotContain(pipeline.Analyze("01週間は番号だ。"), word => word.Surface == "01週間");

        Assert.Contains(pipeline.Analyze("百一度やり直す。"), word => word.Surface == "一度");
        Assert.Contains(pipeline.Analyze("一十分で終わる。"), word => word.Surface == "十分");
    }

    [Fact]
    public void Homographs_context_override_and_single_token_word_pitch_boundaries_hold_end_to_end()
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        // Simulated LLM contextual reading override for the first 額 -> ひたい while keeping the second 額 -> がく.
        var words = pipeline.Analyze("額に汗をかくが、支払う額は大きい。").ToArray();
        var hitaiIndex = Array.FindIndex(words, word => word.Surface == "額");
        var gakuIndex = Array.FindLastIndex(words, word => word.Surface == "額");
        Assert.True(hitaiIndex >= 0 && gakuIndex > hitaiIndex);

        var corrected = words[hitaiIndex].WithOverriddenReading("ひたい");
        pipeline.ResolvePitch(ref corrected);
        Assert.NotNull(corrected.SpanPitch);
        Assert.Equal(SpanPitchSource.Dictionary, corrected.SpanPitch.Source);
        Assert.Equal(0, corrected.SpanPitch.Positions[0]);
        Assert.Null(corrected.WordPitch);
        Assert.Equal("がく", Kana.ToHiragana(words[gakuIndex].Reading));

        // Idioms with JMdict reading override and no whole-phrase Kanjium entry keep WordPitch reference
        // and build per-clause CorrectedPitchAccents for accurate overlay coloring.
        var bow = FindWord(pipeline.Analyze("頭を垂れる。"), "頭を垂れる");
        Assert.Equal("こうべをたれる", Kana.ToHiragana(bow.Reading));
        Assert.True(bow.HasOverriddenReading);
        Assert.Null(bow.SpanPitch);
        Assert.NotNull(bow.WordPitch);
        Assert.Equal("頭", bow.WordPitch.Surface);
        Assert.Equal("こうべ", bow.WordPitch.Reading);
        Assert.Equal([0, 3], bow.WordPitch.Positions);
        Assert.Equal([("頭", (int?)0), ("を", null), ("垂れる", 2)],
            bow.CorrectedPitchAccents.Select(segment => (segment.Surface, segment.AccentPosition)));

        var bowed = FindWord(pipeline.Analyze("頭を垂れた。"), "頭を垂れた");
        Assert.Equal("こうべをたれた", Kana.ToHiragana(bowed.Reading));
        Assert.Null(bowed.SpanPitch);
        Assert.NotNull(bowed.WordPitch);
        Assert.Equal([("頭", (int?)0), ("を", null), ("垂れた", 1)],
            bowed.CorrectedPitchAccents.Select(segment => (segment.Surface, segment.AccentPosition)));

        var returnHeel = FindWord(pipeline.Analyze("踵を返す。"), "踵を返す");
        Assert.Equal("きびすをかえす", Kana.ToHiragana(returnHeel.Reading));
        Assert.Null(returnHeel.SpanPitch);
        Assert.NotNull(returnHeel.WordPitch);
        Assert.Equal([("踵", (int?)0), ("を", null), ("返す", 1)],
            returnHeel.CorrectedPitchAccents.Select(segment => (segment.Surface, segment.AccentPosition)));

        var heel = FindWord(pipeline.Analyze("踵を返して去った。"), "踵を返して");
        Assert.Equal("きびすをかえして", Kana.ToHiragana(heel.Reading));
        Assert.Null(heel.SpanPitch);
        Assert.NotNull(heel.WordPitch);
        Assert.Equal("踵", heel.WordPitch.Surface);
        Assert.Equal("きびす", heel.WordPitch.Reading);
        Assert.Equal([("踵", (int?)0), ("を", null), ("返して", 1)],
            heel.CorrectedPitchAccents.Select(segment => (segment.Surface, segment.AccentPosition)));

        // Homophones with distinct kanji keep isolated dictionary pitch.
        var homophones = pipeline.Analyze("雨と飴、橋と箸と端。");
        Assert.Equal(1, FindWord(homophones, "雨").SpanPitch!.Positions[0]);
        Assert.Equal(0, FindWord(homophones, "飴").SpanPitch!.Positions[0]);
        Assert.Equal(2, FindWord(homophones, "橋").SpanPitch!.Positions[0]);
        Assert.Equal(1, FindWord(homophones, "箸").SpanPitch!.Positions[0]);
        Assert.Equal(0, FindWord(homophones, "端").SpanPitch!.Positions[0]);
    }

    [Theory]
    [InlineData("そろそろ食べよう。", "食べよう", 3)]
    [InlineData("一緒に行こう。", "行こう", 2)]
    [InlineData("食べて、食べた。", "食べて", 1)]
    [InlineData("食べて、食べた。", "食べた", 1)]
    [InlineData("見て、見た。", "見て", 1)]
    [InlineData("見て、見た。", "見た", 1)]
    [InlineData("集めて、書いた、歩いて。", "集めて", 2)]
    [InlineData("集めて、書いた、歩いて。", "書いた", 1)]
    [InlineData("集めて、書いた、歩いて。", "歩いて", 2)]
    [InlineData("飲んで、飲んだ。", "飲んで", 1)]
    [InlineData("飲んで、飲んだ。", "飲んだ", 1)]
    [InlineData("呼んで、呼んだ。", "呼んで", 0)]
    [InlineData("呼んで、呼んだ。", "呼んだ", 0)]
    [InlineData("行って、行った。", "行って", 0)]
    [InlineData("行って、行った。", "行った", 0)]
    [InlineData("買って、買った。", "買って", 0)]
    [InlineData("買って、買った。", "買った", 0)]
    [InlineData("電気をつけて。", "つけて", 2)]
    [InlineData("誰か助けて。", "助けて", 3)]
    [InlineData("私を信じて。", "信じて", 1)]
    public void Verb_te_ta_volitional_and_devoiced_exceptions_resolve_end_to_end(
        string sentence, string targetSurface, int expectedPrimary)
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var word = FindWord(pipeline.Analyze(sentence), targetSurface);
        Assert.NotNull(word.SpanPitch);
        Assert.Equal(expectedPrimary, word.SpanPitch.Positions[0]);
    }

    [Theory]
    [InlineData("食べます。行きます。", "食べます", 3)]
    [InlineData("食べます。行きます。", "行きます", 3)]
    [InlineData("食べません。買いません。", "食べません", 4)]
    [InlineData("食べません。買いません。", "買いません", 4)]
    [InlineData("食べませんでした。", "食べませんでした", 4)]
    [InlineData("食べない。行かない。買わない。", "食べない", 2)]
    [InlineData("食べない。行かない。買わない。", "行かない", 0)]
    [InlineData("食べない。行かない。買わない。", "買わない", 0)]
    [InlineData("食べなかった。行かなかった。買わなかった。", "食べなかった", 2)]
    [InlineData("食べなかった。行かなかった。買わなかった。", "行かなかった", 3)]
    [InlineData("食べなかった。行かなかった。買わなかった。", "買わなかった", 3)]
    [InlineData("食べたい。行きたい。", "食べたい", 3)]
    [InlineData("食べたい。行きたい。", "行きたい", 3)]
    [InlineData("食べたかった。行きたかった。", "食べたかった", 3)]
    [InlineData("食べたかった。行きたかった。", "行きたかった", 3)]
    [InlineData("食べられる。食べさせられる。食べさせられた。", "食べられる", 4)]
    [InlineData("食べられる。食べさせられる。食べさせられた。", "食べさせられる", 6)]
    [InlineData("食べられる。食べさせられる。食べさせられた。", "食べさせられた", 5)]
    [InlineData("食べたら。行ったら。", "食べたら", 1)]
    [InlineData("食べたら。行ったら。", "行ったら", 3)]
    public void Multi_layer_verb_auxiliary_chains_resolve_end_to_end(
        string sentence, string targetSurface, int expectedPrimary)
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var word = FindWord(pipeline.Analyze(sentence), targetSurface);
        Assert.NotNull(word.SpanPitch);
        Assert.Equal(expectedPrimary, word.SpanPitch.Positions[0]);
    }

    [Theory]
    [InlineData("高かった。赤かった。白かった。面白かった。", "高かった", 2)]
    [InlineData("高かった。赤かった。白かった。面白かった。", "赤かった", 2)]
    [InlineData("高かった。赤かった。白かった。面白かった。", "白かった", 2)]
    [InlineData("高かった。赤かった。白かった。面白かった。", "面白かった", 4)]
    [InlineData("高くて。赤くて。", "高くて", 2)]
    [InlineData("高くて。赤くて。", "赤くて", 2)]
    [InlineData("高いです。赤いです。", "高いです", 2)]
    [InlineData("高いです。赤いです。", "赤いです", 2)]
    [InlineData("論理的だ。物理学を学ぶ。", "論理的", 0)]
    [InlineData("論理的だ。物理学を学ぶ。", "物理学", 3)]
    [InlineData("食べやすい。食べすぎる。食べすぎた。食べやすかった。", "食べやすい", 4)]
    [InlineData("食べやすい。食べすぎる。食べすぎた。食べやすかった。", "食べすぎる", 4)]
    [InlineData("食べやすい。食べすぎる。食べすぎた。食べやすかった。", "食べすぎた", 3)]
    [InlineData("食べやすい。食べすぎる。食べすぎた。食べやすかった。", "食べやすかった", 4)]
    [InlineData("食べやすくて食べやすいです。", "食べやすくて", 4)]
    [InlineData("食べやすくて食べやすいです。", "食べやすいです", 4)]
    [InlineData("お寿司とお米とお茶碗。", "お米", 0)]
    [InlineData("スイカかお茶かご飯。", "お茶", 0)]
    [InlineData("スイカかお茶かご飯。", "ご飯", 1)]
    [InlineData("お米、お水、お弁当、お菓子。", "お水", 0)]
    [InlineData("お米、お水、お弁当、お菓子。", "お弁当", 0)]
    [InlineData("お米、お水、お弁当、お菓子。", "お菓子", 2)]
    [InlineData("今テレビを見ている。", "テレビ", 1)]
    [InlineData("今テレビを見ている。", "見て", 1)]
    public void Adjectives_affixes_and_lexical_compounds_resolve_end_to_end(
        string sentence, string targetSurface, int expectedPrimary)
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var word = FindWord(pipeline.Analyze(sentence), targetSurface);
        Assert.NotNull(word.SpanPitch);
        Assert.Equal(expectedPrimary, word.SpanPitch.Positions[0]);
    }

    [Fact]
    public void Particle_before_honorific_prefix_and_dictionary_adjectival_suffix_compounds_preserve_exact_boundaries()
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var prefixWords = pipeline.Analyze("お寿司とお米とお茶碗。");
        Assert.DoesNotContain(prefixWords, word => word.Surface == "とお");
        Assert.Contains(prefixWords, word => word.Surface == "お米");

        var kanaCollisionWords = pipeline.Analyze("スイカかお茶かご飯。");
        Assert.DoesNotContain(kanaCollisionWords, word => word.Surface is "かお" or "かご");
        Assert.Contains(kanaCollisionWords, word => word.Surface == "お茶");
        Assert.Contains(kanaCollisionWords, word => word.Surface == "ご飯");

        var dictCompound = FindWord(pipeline.Analyze("分かりやすかった。"), "分かりやすかった");
        Assert.Equal("分かりやすい", dictCompound.LookupKey);
        Assert.NotNull(dictCompound.SpanPitch);
        Assert.Equal(5, dictCompound.SpanPitch.Positions[0]);
    }

    [Theory]
    [InlineData("お寿司", 2, SpanPitchSource.MorphologicalRule)]
    [InlineData("お茶碗", 0, SpanPitchSource.MorphologicalRule)]
    public void Real_unidic_tokens_for_unmerged_affixes_resolve_accurately_when_grouped(
        string phrase, int expectedPrimary, SpanPitchSource expectedSource)
    {
        using var pipeline = TryCreatePipeline();
        if (pipeline is null) return;

        var pitch = pipeline.ResolveAsSingleGroupedSpan(phrase);
        Assert.NotNull(pitch);
        Assert.Equal(expectedSource, pitch.Source);
        Assert.Equal(expectedPrimary, pitch.Positions[0]);
    }

    private static GroupedWord FindWord(IReadOnlyList<GroupedWord> words, string surface)
    {
        var match = words.FirstOrDefault(word => word.Surface == surface);
        Assert.True(match is not null,
            $"Expected grouped word '{surface}', actual spans: [{string.Join(", ", words.Select(w => $"{w.Surface}({Kana.ToHiragana(w.Reading)})"))}]");
        return match!;
    }

    private static LocalPitchPipeline? TryCreatePipeline()
    {
        var unidic = TryResolveUniDic();
        var jmdict = TryResolveDictionaryAsset("jmdict.db");
        var pitchDb = TryResolveDictionaryAsset("pitch-accent.db");
        if (unidic is null || jmdict is null || pitchDb is null)
            return null;

        return new LocalPitchPipeline(unidic, jmdict, pitchDb);
    }

    private static string? TryResolveUniDic()
    {
        var env = Environment.GetEnvironmentVariable("KOTOBA_UNIDIC_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);
        var localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai", "UniDic", "dicdir");
        if (Directory.Exists(localAppData))
            return localAppData;
        var doki = @"C:\Program Files (x86)\DokiDokiDict\_internal\unidic\dicdir";
        return Directory.Exists(doki) ? doki : null;
    }

    private static string? TryResolveDictionaryAsset(string fileName)
    {
        var besideAssembly = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(besideAssembly))
            return besideAssembly;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "KotobaSenpai.App", "Assets", "Dictionary", fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private sealed class LocalPitchPipeline : IDisposable
    {
        private readonly UniDicTokenizer _tokenizer;
        private readonly WordGroupingService _grouping;
        private readonly SpanPitchResolver _spanPitchResolver;

        public LocalPitchPipeline(string unidicDir, string jmdictDbPath, string pitchDbPath)
        {
            var logger = new NullLogger();
            _tokenizer = new UniDicTokenizer(logger, unidicDir);
            var jmdict = new JmdictLookupService(new JmdictSqliteRepository(jmdictDbPath, logger));
            var spanResolver = new TokenBoundarySpanResolver(jmdict);
            _grouping = new WordGroupingService(_tokenizer, new SentenceSegmenter(), spanResolver);
            var pitchDictionary = new PitchAccentSqliteRepository(pitchDbPath, logger);
            _spanPitchResolver = new SpanPitchResolver(pitchDictionary);
        }

        public IReadOnlyList<GroupedWord> Analyze(string text)
        {
            var line = new OcrLine(text.Select((ch, i) =>
                new OcrWord(ch.ToString(), new ScreenRect(i * 20, 0, 20, 30))).ToArray());
            var words = _grouping.Group([line]).ToArray();
            ApplyPitch(words);
            return words;
        }

        public void ResolvePitch(ref GroupedWord word)
        {
            var array = new[] { word };
            ApplyPitch(array);
            word = array[0];
        }

        public SpanPitch? ResolveAsSingleGroupedSpan(string phrase)
        {
            var tokens = _tokenizer.Tokenize(phrase);
            Assert.NotEmpty(tokens);
            var word = new GroupedWord(
                new LookupSpan(tokens, string.Concat(tokens.Select(t => t.Surface)), []),
                new ScreenRect(0, 0, Math.Max(1, phrase.Length) * 20, 30));
            return _spanPitchResolver.Resolve([word])[0];
        }

        private void ApplyPitch(GroupedWord[] words)
        {
            var pitches = _spanPitchResolver.Resolve(words);
            for (var i = 0; i < words.Length; i++)
            {
                words[i] = pitches[i] is { } pitch
                    ? words[i] with { SpanPitch = pitch }
                    : words[i].WithDictionaryWordPitch(_tokenizer);
            }
        }

        public void Dispose() => _tokenizer.Dispose();
    }

    private sealed class NullLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    }
}

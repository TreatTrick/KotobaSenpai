using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Platform.Windows;
using KotobaSenpai.Platform.Windows.Japanese;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>
/// UniDic tokenizer tests. The missing-dictionary / invalid-version tests always run (no real dictionary needed);
/// the golden-corpus tests require a local UniDic dictionary (env var <c>KOTOBA_UNIDIC_DIR</c> or the machine's DokiDokiDict directory),
/// otherwise they run empty and are skipped — the dictionary is not in git and tests do not download over the network.
/// The dictionary-parsing and tokenizer <see cref="UniDicDictionaryMissing"/>/<see cref="UniDicDictionaryInvalid"/> branches are always covered.
/// </summary>
public sealed class UniDicTokenizerTests
{
    private static readonly ILogger Logger = new NullLogger();

    [Fact]
    public void Missing_runtime_files_throws_unicdic_dictionary_missing()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "unicdic-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var tokenizer = new UniDicTokenizer(Logger, emptyDir);
            var ex = Assert.Throws<WindowsPlatformException>(() => tokenizer.Tokenize("日本語"));
            Assert.Equal(ErrorCodes.UniDicDictionaryMissing, ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void Invalid_format_throws_unicdic_dictionary_invalid()
    {
        var dir = Path.Combine(Path.GetTempPath(), "unicdic-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // All four runtime files present, but dicrc does not use the unidic22 format → Invalid (not Missing).
            foreach (var f in UniDicAssets.RequiredRuntimeFiles)
                File.WriteAllText(Path.Combine(dir, f), "x");
            File.WriteAllText(Path.Combine(dir, UniDicAssets.DicrcFileName), "output-format-type = mecab-ipadic");

            var tokenizer = new UniDicTokenizer(Logger, dir);
            var ex = Assert.Throws<WindowsPlatformException>(() => tokenizer.Tokenize("日本語"));
            Assert.Equal(ErrorCodes.UniDicDictionaryInvalid, ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Null_empty_whitespace_returns_empty_without_dictionary()
    {
        // Empty input returns an empty list before touching the tagger, so no real dictionary is needed.
        var tokenizer = new UniDicTokenizer(Logger, Path.GetTempPath());
        Assert.Empty(tokenizer.Tokenize(null));
        Assert.Empty(tokenizer.Tokenize(string.Empty));
        Assert.Empty(tokenizer.Tokenize("  \n\t "));
    }

    [Fact]
    public void Golden_sentence_returns_expected_fields_and_offsets()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return; // no dictionary, skip (CI does not download)

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var tokens = tokenizer.Tokenize("日本語の解析テストです。");

        Assert.Equal(["日本", "語", "の", "解析", "テスト", "です", "。"],
            tokens.Select(t => t.Surface));

        // Verified golden values: surface / offset / lemma / reading / part of speech.
        Assert.Equal("日本", tokens[0].Surface);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal("ニッポン", tokens[0].Reading);
        Assert.Equal("解析", tokens[3].Surface);
        Assert.Equal(4, tokens[3].StartOffset);
        Assert.Equal(new PartsOfSpeech("名詞", "普通名詞", "サ変可能", "*"), tokens[3].PartsOfSpeech);

        // Each token's source-text slice must match its surface (offsets are UTF-16 code-unit starts).
        const string input = "日本語の解析テストです。";
        foreach (var t in tokens)
            Assert.Equal(t.Surface, input.Substring(t.StartOffset, t.Surface.Length));
    }

    [Fact]
    public void Conjugated_verb_reports_lemma_separate_reading_fields()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return;

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var tokens = tokenizer.Tokenize("買った");
        var buy = tokens.Single(t => t.Surface == "買っ");

        Assert.Equal("買う", buy.Lemma);            // 語彙素 = 辞书形
        Assert.Equal("買う", buy.OrthBase);          // 書字形基本形
        Assert.Equal("カッ", buy.Reading);           // 仮名形出現形
        Assert.Equal("カウ", buy.BaseReading);       // 仮名形基本形
        Assert.Equal("カッ", buy.Pronunciation);     // 発音形出現形
        Assert.Equal("動詞", buy.PartsOfSpeech.Pos1);
        Assert.Equal("五段-ワア行", buy.ConjugationType);
        Assert.Equal("連用形-促音便", buy.ConjugationForm);
    }

    [Fact]
    public void UniDic310_segmentation_splits_aluminum_foil()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return;

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var tokens = tokenizer.Tokenize("アルミホイルを買った");
        // UniDic 3.1.0 (a 2.3.0 revision) splits foreign-language abbreviations into two short units.
        Assert.Contains(tokens, t => t.Surface == "アルミ");
        Assert.Contains(tokens, t => t.Surface == "ホイル");
    }

    [Fact]
    public void Multi_value_accent_field_is_preserved_as_raw()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return;

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var tokens = tokenizer.Tokenize("覆うふりをした。");
        var cover = tokens.Single(t => t.Surface == "覆う");

        // The strongly-typed property decodes the aType multi-value field (with quotes/commas), keeping it as the original string.
        Assert.Equal("0,2", cover.AType);
    }

    [Fact]
    public void Whitespace_offsets_are_utf16_code_units()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return;

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var tokens = tokenizer.Tokenize("  日本\n語").ToArray();

        Assert.Equal("日本", tokens[0].Surface);
        Assert.Equal(2, tokens[0].StartOffset);   // two leading spaces
        Assert.Equal("語", tokens[1].Surface);
        Assert.Equal(5, tokens[1].StartOffset);   // 2 + 2 + 1(\n)
    }

    [Fact]
    public void Concurrent_tokenization_is_deterministic()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return;

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var sentence = "日本語の解析テストです。";
        var expected = tokenizer.Tokenize(sentence).ToArray();

        // Concurrent use of the same single instance: results match isolated calls, no exceptions, no field cross-talk.
        Parallel.For(0, 64, _ =>
        {
            var actual = tokenizer.Tokenize(sentence).ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].Surface, actual[i].Surface);
                Assert.Equal(expected[i].Lemma, actual[i].Lemma);
                Assert.Equal(expected[i].Reading, actual[i].Reading);
                Assert.Equal(expected[i].StartOffset, actual[i].StartOffset);
            }
        });
    }

    private static string? TryResolveDictionary()
    {
        var env = Environment.GetEnvironmentVariable("KOTOBA_UNIDIC_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        var doki = @"C:\Program Files (x86)\DokiDokiDict\_internal\unidic\dicdir";
        return Directory.Exists(doki) ? doki : null;
    }

    private sealed class NullLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    }
}
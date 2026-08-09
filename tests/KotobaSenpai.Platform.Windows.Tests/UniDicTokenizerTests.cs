using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Platform.Windows;
using KotobaSenpai.Platform.Windows.Japanese;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>
/// UniDic 分词器测试。缺词典/版本错误测试始终运行（无需真实词典）；
/// 黄金语料测试需本地具备 UniDic 词典（环境变量 <c>KOTOBA_UNIDIC_DIR</c> 或本机 DokiDokiDict 目录），
/// 否则空跑跳过——词典不入 git、测试不联网下载。词典解析与 tokenizer 的
/// <see cref="UniDicDictionaryMissing"/>/<see cref="UniDicDictionaryInvalid"/> 分支始终覆盖。
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
            // 四个运行时文件齐，但 dicrc 不含 unidic22 格式 → Invalid（而非 Missing）。
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
        // 空输入在触碰 tagger 前返回空列表，无需真实词典。
        var tokenizer = new UniDicTokenizer(Logger, Path.GetTempPath());
        Assert.Empty(tokenizer.Tokenize(null));
        Assert.Empty(tokenizer.Tokenize(string.Empty));
        Assert.Empty(tokenizer.Tokenize("  \n\t "));
    }

    [Fact]
    public void Golden_sentence_returns_expected_fields_and_offsets()
    {
        var dict = TryResolveDictionary();
        if (dict is null) return; // 无词典则跳过（CI 不联网下载）

        var tokenizer = new UniDicTokenizer(Logger, dict);
        var tokens = tokenizer.Tokenize("日本語の解析テストです。");

        Assert.Equal(["日本", "語", "の", "解析", "テスト", "です", "。"],
            tokens.Select(t => t.Surface));

        // 已验证的黄金值：词面/偏移/词元/读音/词性。
        Assert.Equal("日本", tokens[0].Surface);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal("ニッポン", tokens[0].Reading);
        Assert.Equal("解析", tokens[3].Surface);
        Assert.Equal(4, tokens[3].StartOffset);
        Assert.Equal(new PartsOfSpeech("名詞", "普通名詞", "サ変可能", "*"), tokens[3].PartsOfSpeech);

        // 每个 token 的源文本切片必须与其词面一致（校验偏移为 UTF-16 起点）。
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
        // UniDic 3.1.0（2.3.0 修订）把外来语缩写按两短单位切分。
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

        // 强类型属性解码带引号/逗号的 aType 多值字段，保持为原始字符串。
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
        Assert.Equal(2, tokens[0].StartOffset);   // 前导两空格
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

        // 同一单例多线程并发：结果与孤立调用一致、无异常、无字段串扰。
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
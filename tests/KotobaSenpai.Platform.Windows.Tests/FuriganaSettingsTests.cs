using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>Pure logic of the furigana overlay: kanji detection and font-scale parsing.</summary>
public class FuriganaSettingsTests
{
    [Theory]
    [InlineData("日本語", true)]
    [InlineData("あります", false)]
    [InlineData("カタカナ", false)]
    [InlineData("hello", false)]
    public void ContainsKanji_detects_only_kanji(string text, bool expected)
        => Assert.Equal(expected, FuriganaSettings.ContainsKanji(text));

    [Theory]
    [InlineData(null, FuriganaSettings.DefaultFontScale)]
    [InlineData("", FuriganaSettings.DefaultFontScale)]
    [InlineData("abc", FuriganaSettings.DefaultFontScale)]
    [InlineData("0", FuriganaSettings.DefaultFontScale)]
    [InlineData("-0.5", FuriganaSettings.DefaultFontScale)]
    [InlineData("0.25", 0.25)]
    [InlineData("0.5", 0.5)]
    public void ResolveFontScale_falls_back_on_missing_invalid_and_uses_valid(string? raw, double expected)
        => Assert.Equal(expected, FuriganaSettings.ResolveFontScale(raw));

    [Theory]
    [InlineData("同じ", "おなじ", 1, "おな")]      // 送假名「じ」不标注
    [InlineData("食べる", "たべる", 1, "た")]      // 送假名「べる」不标注
    [InlineData("日本語", "にほんご", 3, "にほんご")] // 无送假名，全读音
    [InlineData("今日", "きょう", 2, "きょう")]    // 无送假名
    [InlineData("買い物", "かいもの", 1, "かいもの")] // 中部送假名词（汉字-假名-汉字）不剥离尾部，整词读音（同 DokiDokiDict）
    public void OkuriganaTrim_annotates_only_leading_kanji(string surface, string reading, int kanjiChars, string kanjiReading)
    {
        var (count, trimmed) = FuriganaSettings.OkuriganaTrim(surface, reading);
        Assert.Equal(kanjiChars, count);
        Assert.Equal(kanjiReading, trimmed);
    }
}
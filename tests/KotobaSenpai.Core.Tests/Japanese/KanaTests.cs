using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.Tests.Japanese;

public sealed class KanaTests
{
    [Theory]
    [InlineData("つきる", "tsukiru")]
    [InlineData("ツキササッテ", "tsukisasatte")]
    [InlineData("のみこんでいく", "nomikondeiku")]
    [InlineData("がっこう", "gakkou")]
    [InlineData("きょう", "kyou")]
    [InlineData("しんよう", "shin'you")]
    [InlineData("シンイ", "shin'i")]
    [InlineData("まっちゃ", "matcha")]
    [InlineData("コーヒー", "koohii")]
    [InlineData("ティッシュ", "tisshu")]
    [InlineData("パーティー", "paatii")]
    [InlineData("ファイル", "fairu")]
    [InlineData("ヴァイオリン", "vaiorin")]
    [InlineData("ｶﾞｯｺｳ", "gakkou")]
    [InlineData("Hello, かな!", "Hello, kana!")]
    [InlineData("", "")]
    public void Romanization_handles_readings_compounds_and_pronunciation_markers(string reading, string expected)
        => Assert.Equal(expected, Kana.ToRomaji(reading));

    [Fact]
    public void Converts_katakana_reading_to_hiragana()
    {
        Assert.Equal("そんな", Kana.ToHiragana("ソンナ"));
        Assert.Equal("かんけいない", Kana.ToHiragana("カンケイナイ"));
        Assert.Equal("えるおー", Kana.ToHiragana("エルオー"));
    }

    [Fact]
    public void Leaves_hiragana_and_latin_unchanged()
    {
        Assert.Equal("そんな", Kana.ToHiragana("そんな"));
        Assert.Equal("LO", Kana.ToHiragana("LO"));
    }
}

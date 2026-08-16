using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Core.Tests;

public sealed class KanaTests
{
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
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Core.Tests;

public sealed class PitchAccentTests
{
    [Fact]
    public void Parses_first_valid_accent_candidate_and_rejects_invalid_values()
    {
        Assert.Equal(0, PitchAccent.ParsePosition("0,2", "覆う"));
        Assert.Null(PitchAccent.ParsePosition("*", "覆う"));
        Assert.Null(PitchAccent.ParsePosition("9", "覆う"));
    }

    [Fact]
    public void Splits_small_kana_into_morae()
    {
        Assert.Equal(["きょ", "う"], PitchAccent.SplitMorae("きょう"));
        Assert.Equal(["が", "っ", "こ", "う"], PitchAccent.SplitMorae("がっこう"));
    }

    [Theory]
    [InlineData(0, "LHH", "[0] LHH")]
    [InlineData(1, "HLL", "[1] H↓LL")]
    [InlineData(2, "LHLL", "[2] LH↓LL")]
    [InlineData(4, "LHHH", "[4] LHHH↓")]
    public void Builds_tokyo_pitch_pattern_and_notation(
        int accentPosition,
        string expectedLetters,
        string expectedNotation)
    {
        var pattern = PitchAccent.BuildPattern(accentPosition, expectedLetters.Length);

        Assert.Equal(expectedLetters, string.Concat(pattern.Select(high => high ? "H" : "L")));
        Assert.Equal(expectedNotation, PitchAccent.Format(accentPosition, pattern));
    }

    [Fact]
    public void Token_exposes_normalized_pitch_without_losing_raw_a_type()
    {
        var token = new Token(
            "覆う", "覆う", "覆う", "おおう", "おおう", "オオウ",
            new PartsOfSpeech("動詞", "", "", ""), "", "", "0,2", 0);

        Assert.Equal("0,2", token.AType);
        Assert.Equal(0, token.PitchAccentPosition);
    }

    [Fact]
    public void Merged_span_exposes_pitch_for_each_source_token()
    {
        var first = new Token(
            "東京", "東京", "東京", "とうきょう", "とうきょう", "トウキョウ",
            new PartsOfSpeech("助詞", "", "", ""), "", "", "1", 0);
        var second = new Token(
            "で", "で", "で", "で", "で", "デ",
            new PartsOfSpeech("助詞", "", "", ""), "", "", "0", 1);
        var span = new LookupSpan([first, second], "東京で", []);

        Assert.Collection(span.PitchAccents,
            firstPitch =>
            {
                Assert.Equal("東京", firstPitch.Surface);
                Assert.Equal(0, firstPitch.SurfaceOffset);
                Assert.Equal(1, firstPitch.AccentPosition);
                Assert.Equal("[1] H↓LLL", firstPitch.Notation);
            },
            secondPitch =>
            {
                Assert.Equal("で", secondPitch.Surface);
                Assert.Equal(2, secondPitch.SurfaceOffset);
                Assert.Equal(0, secondPitch.AccentPosition);
                Assert.Equal("[0] H", secondPitch.Notation);
            });

        var grouped = new GroupedWord(span, new ScreenRect(0, 0, 30, 20));
        Assert.Equal(span.PitchAccents, grouped.PitchAccents);
    }
}

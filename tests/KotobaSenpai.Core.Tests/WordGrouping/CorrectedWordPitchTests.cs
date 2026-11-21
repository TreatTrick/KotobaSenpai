using KotobaSenpai.Core.Dictionary;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Core.Tests.WordGrouping;

public sealed class CorrectedWordPitchTests
{
    [Theory]
    [InlineData("頭", "アタマ", "垂れる", "タレル", "こうべをたれる", "こうべ")]
    [InlineData("踵", "カカト", "返す", "カエス", "きびすをかえす", "きびす")]
    public void Keeps_dictionary_word_pitch_separate_from_the_phrase_pitch(string noun, string nounReading,
        string verb, string verbReading, string correctedReading, string correctedNounReading)
    {
        var word = CreateWord([CreateToken(noun, nounReading, 0), CreateToken("を", "ヲ", 1),
            CreateToken(verb, verbReading, 2)], correctedReading);
        var lookup = new RecordingLookup([0]);

        var pitch = Assert.IsType<WordPitchReference>(CorrectedWordPitch.Find(word, lookup));

        Assert.True(word.HasOverriddenReading);
        Assert.Empty(word.PitchAccents);
        Assert.Equal(correctedReading, word.Reading);
        Assert.Equal((noun, correctedNounReading), Assert.Single(lookup.Calls));
        Assert.Equal(noun, pitch.Surface);
        Assert.Equal(correctedNounReading, pitch.Reading);
        Assert.Equal([0], pitch.Positions);
    }

    [Fact]
    public void Does_not_look_up_pitch_for_an_ambiguous_alignment()
    {
        var word = CreateWord([CreateToken("阿", "ア", 0), CreateToken("亜", "ア", 1)], "あああ");
        var lookup = new RecordingLookup([0]);

        Assert.Null(CorrectedWordPitch.Find(word, lookup));
        Assert.Empty(lookup.Calls);
    }

    [Fact]
    public void Leaves_pitch_unknown_when_the_corrected_reading_has_no_dictionary_pitch()
    {
        var word = CreateWord([CreateToken("頭", "アタマ", 0)], "こうべ");
        var lookup = new RecordingLookup([]);

        Assert.Null(CorrectedWordPitch.Find(word, lookup));
        Assert.Equal(("頭", "こうべ"), Assert.Single(lookup.Calls));
    }

    private static GroupedWord CreateWord(Token[] tokens, string correctedReading)
        => new(new LookupSpan(tokens, string.Concat(tokens.Select(token => token.Surface)), [],
            overriddenReading: correctedReading), new ScreenRect(0, 0, 100, 20));

    private static Token CreateToken(string surface, string reading, int offset)
        => new(surface, surface, surface, reading, reading, reading, new("Noun", "", "", ""), "", "", "2", offset);

    private sealed class RecordingLookup(IReadOnlyList<int> positions) : IWordPitchLookup
    {
        public List<(string Surface, string Reading)> Calls { get; } = [];

        public IReadOnlyList<int> FindWordPitch(Token source, string reading)
        {
            Calls.Add((source.Surface, reading));
            return positions;
        }
    }
}

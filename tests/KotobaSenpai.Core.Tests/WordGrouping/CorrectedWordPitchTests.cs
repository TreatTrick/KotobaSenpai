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

        var resolved = word.WithDictionaryWordPitch(lookup);
        Assert.Null(resolved.WordPitch);
        Assert.Empty(resolved.CorrectedPitchAccents);
        Assert.Equal(("頭", "こうべ"), Assert.Single(lookup.Calls));
    }

    [Fact]
    public void Builds_per_clause_corrected_pitch_segments_including_unchanged_morphological_inflections()
    {
        var word = CreateWord(
        [
            new Token("頭", "頭", "頭", "アタマ", "アタマ", "アタマ", new("名詞", "普通名詞", "", ""), "", "", "2", 0),
            new Token("を", "を", "を", "ヲ", "ヲ", "オ", new("助詞", "格助詞", "", ""), "", "", "*", 1),
            new Token("垂れ", "垂れる", "垂れる", "タレ", "タレル", "タレ", new("動詞", "一般", "", ""), "下一段-ラ行", "連用形-一般", "2", 2, "C1", "M4@1"),
            new Token("た", "た", "た", "タ", "タ", "タ", new("助動詞", "", "", ""), "助動詞-タ", "終止形-一般", "*", 4, "動詞%F1", "*"),
        ], "こうべをたれた");

        var resolved = word.WithDictionaryWordPitch(new RecordingLookup([0, 3]));

        Assert.NotNull(resolved.WordPitch);
        Assert.Empty(resolved.PitchAccents);
        Assert.Collection(resolved.CorrectedPitchAccents,
            noun =>
            {
                Assert.Equal("頭", noun.Surface);
                Assert.Equal("こうべ", noun.Reading);
                Assert.Equal(0, noun.SurfaceOffset);
                Assert.Equal(0, noun.AccentPosition);
            },
            particle =>
            {
                Assert.Equal("を", particle.Surface);
                Assert.Equal(1, particle.SurfaceOffset);
                Assert.Null(particle.AccentPosition);
            },
            verb =>
            {
                Assert.Equal("垂れた", verb.Surface);
                Assert.Equal("たれた", verb.Reading);
                Assert.Equal(2, verb.SurfaceOffset);
                Assert.Equal(1, verb.AccentPosition);
            });
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

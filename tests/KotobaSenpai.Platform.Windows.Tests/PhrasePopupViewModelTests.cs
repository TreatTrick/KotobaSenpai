using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class PhrasePopupViewModelTests
{
    [Fact]
    public void Phrase_members_preserve_order_and_detach_from_mutable_input()
    {
        var pitches = new List<PitchAccentSummary> { new("学校", "ガッコウ", 0, 0, "[0] LHHH") };
        var members = new[]
        {
            new WordMeaningView("学校", "ガッコウ", "Noun", "School", "", pitches),
            new WordMeaningView("Second", "", "", "", ""),
        };

        var model = PhrasePopupViewModel.ForPhrase("Phrase", "Group meaning", "Group grammar", members);
        pitches.Clear();
        members[0] = members[1];

        Assert.Equal("Phrase", model.Heading);
        Assert.Equal("Group meaning", model.Meaning);
        Assert.Equal("Group grammar", model.Grammar);
        Assert.Empty(model.Pos);
        Assert.Empty(model.Pitch);
        Assert.Empty(model.NoMeaningHint);
        Assert.Equal("Words", model.WordsLabel);
        Assert.Equal(["学校", "Second"], model.Words.Select(word => word.Headword));
        Assert.Equal("がっこう", model.Words[0].Reading);
        Assert.Equal("⓪", model.Words[0].PitchMarker);
        Assert.Equal("gakkou", model.Words[0].Romanization);
        Assert.Equal("がっこう  |  gakkou", model.Words[0].Pronunciation);
        Assert.Equal("Pitch LHHH", model.Words[0].Pitch);
        Assert.Equal("Noun", model.Words[0].Pos);
        Assert.Equal("School", model.Words[0].Meaning);
        Assert.Empty(model.Words[1].Reading);
        Assert.Empty(model.Words[1].Pos);
        Assert.Equal("pitch unknown", model.Words[1].Pitch);
        Assert.Throws<NotSupportedException>(() => ((IList<PhrasePopupWordViewModel>)model.Words).Clear());
    }

    [Fact]
    public void Word_details_keep_hiragana_and_ordered_pitch_notation()
    {
        var word = new WordMeaningView("学校へ", "ガッコウヘ", "Expression", "To school", "Direction",
        [
            new("学校", "ガッコウ", 0, 0, "[0] LHHH"),
            new("へ", "ヘ", 2, null, null),
            new("へ", "ヘ", 2, 1, "[1] H"),
        ]);

        var model = PhrasePopupViewModel.ForWord(word);

        Assert.Equal("学校へ", model.Heading);
        Assert.Equal("がっこうへ", model.Reading);
        Assert.Equal("Expression", model.Pos);
        Assert.Equal("⓪①", model.PitchMarker);
        Assert.Equal("Pitch LHHH | H↓", model.Pitch);
        Assert.Equal("gakkouhe", model.Romanization);
        Assert.Equal("がっこうへ  |  gakkouhe", model.Pronunciation);
        Assert.Equal("To school", model.Meaning);
        Assert.Equal("Direction", model.Grammar);
        Assert.Equal("Meaning", model.MeaningLabel);
        Assert.Equal("Grammar", model.GrammarLabel);
        Assert.Empty(model.Words);
        Assert.Empty(model.NoMeaningHint);
    }

    [Fact]
    public void Missing_meaning_mode_suppresses_provider_fields()
    {
        var word = new WordMeaningView("Word", "", "Noun", "Unused meaning", "Unused grammar");

        var model = PhrasePopupViewModel.ForWord(word, showMeaning: false);

        Assert.Equal("Word", model.Heading);
        Assert.Empty(model.Reading);
        Assert.Equal("pitch unknown", model.Pitch);
        Assert.Equal("no meaning", model.NoMeaningHint);
        Assert.Empty(model.Pos);
        Assert.Empty(model.Meaning);
        Assert.Empty(model.Grammar);
        Assert.Empty(model.Words);
    }

    [Fact]
    public void Absent_optional_sections_remain_empty()
    {
        var phrase = PhrasePopupViewModel.ForPhrase("Phrase", "", "");
        var word = PhrasePopupViewModel.ForWord(new WordMeaningView("Word", "", "", "", ""));

        Assert.Empty(phrase.Meaning);
        Assert.Empty(phrase.Grammar);
        Assert.Empty(phrase.Words);
        Assert.Empty(word.Pos);
        Assert.Empty(word.Meaning);
        Assert.Empty(word.Grammar);
        Assert.Empty(word.NoMeaningHint);
        Assert.Equal("pitch unknown", word.Pitch);
    }

    [Fact]
    public void Each_snapshot_resolves_current_labels_without_mutating_previous_content()
    {
        var localizer = new TestLocalizer();
        var word = new WordMeaningView("Word", "", "Noun", "Meaning text", "Grammar text");
        var phrase = PhrasePopupViewModel.ForPhrase("Phrase", "Meaning text", "Grammar text", [word], localizer);
        var rich = PhrasePopupViewModel.ForWord(word, localizer);
        localizer.Prefix = "Updated";
        var fallback = PhrasePopupViewModel.ForWord(word, localizer, showMeaning: false);

        Assert.Equal("Localized:Llm.WordsLabel", phrase.WordsLabel);
        Assert.Equal("Localized:Llm.WordMeaningLabel", phrase.MeaningLabel);
        Assert.Equal("Localized:Llm.GrammarLabel", phrase.GrammarLabel);
        Assert.Equal("Localized:Llm.GrammarLabel", rich.GrammarLabel);
        Assert.Equal("Localized:Llm.WordMeaningLabel", rich.MeaningLabel);
        Assert.Equal("Localized:Llm.WordPitchUnknown", rich.Pitch);
        Assert.Equal("Updated:Llm.WordPitchUnknown", fallback.Pitch);
        Assert.Equal("Updated:Llm.WordNoMeaning", fallback.NoMeaningHint);
        Assert.Empty(fallback.Meaning);
        Assert.Equal("Meaning text", rich.Meaning);
        Assert.Single(phrase.Words);
    }

    [Fact]
    public void Phrase_pronunciation_uses_source_reading_and_never_shows_word_metadata()
    {
        var model = PhrasePopupViewModel.ForPhrase("飲み込んでいく", "Meaning", "Grammar",
            reading: "ノミコンデイク");

        Assert.Equal("のみこんでいく", model.Reading);
        Assert.Equal("nomikondeiku", model.Romanization);
        Assert.Equal("のみこんでいく  |  nomikondeiku", model.Pronunciation);
        Assert.Empty(model.PitchMarker);
        Assert.Empty(model.Pos);
        Assert.Empty(model.Pitch);
    }

    [Fact]
    public void Known_word_pitch_has_a_header_marker_and_a_localized_contour_without_bracketed_numbers()
    {
        var word = new WordMeaningView("突き刺さって", "ツキササッテ", "Verb", "Pierce", "",
            [new("突き刺さって", "ツキササッテ", 0, 4, "[4] LHHH↓LL")]);
        var model = PhrasePopupViewModel.ForWord(word, new TestLocalizer());

        Assert.Equal("④", model.PitchMarker);
        Assert.Equal("つきささって  |  tsukisasatte", model.Pronunciation);
        Assert.Equal("Localized:Llm.WordPitchLabel LHHH↓LL", model.Pitch);
        Assert.DoesNotContain("[4]", model.Pitch);
    }

    [Fact]
    public void Unknown_pitch_and_absent_readings_never_create_a_badge_or_separator()
    {
        var model = PhrasePopupViewModel.ForWord(new WordMeaningView("Word", "", "", "", ""));

        Assert.Empty(model.PitchMarker);
        Assert.Empty(model.Romanization);
        Assert.Empty(model.Pronunciation);
    }

    private sealed class TestLocalizer : IStringLocalizer
    {
        public string Prefix { get; set; } = "Localized";
        public string Get(string key, params object[] args) => $"{Prefix}:{key}";
        public event EventHandler? CultureChanged { add { } remove { } }
    }
}

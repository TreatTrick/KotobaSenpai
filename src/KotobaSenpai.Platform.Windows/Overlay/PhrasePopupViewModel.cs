using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>A complete, read-only presentation snapshot for one popup display.</summary>
internal sealed class PhrasePopupViewModel
{
    private PhrasePopupViewModel() { }

    public string Heading { get; private init; } = "";
    public string Reading { get; private init; } = "";
    public string Pos { get; private init; } = "";
    public string Pitch { get; private init; } = "";
    public string Meaning { get; private init; } = "";
    public string Grammar { get; private init; } = "";
    public string NoMeaningHint { get; private init; } = "";
    public string PosLabel { get; private init; } = "Pos";
    public string PitchLabel { get; private init; } = "Pitch";
    public string MeaningLabel { get; private init; } = "Meaning";
    public string GrammarLabel { get; private init; } = "Grammar";
    public string WordsLabel { get; private init; } = "Words";
    public IReadOnlyList<PhrasePopupWordViewModel> Words { get; private init; } = Array.Empty<PhrasePopupWordViewModel>();

    public static PhrasePopupViewModel ForPhrase(
        string label, string meaning, string grammar,
        IReadOnlyList<WordMeaningView>? memberMeanings = null, IStringLocalizer? localizer = null)
        => new()
        {
            Heading = label,
            Meaning = meaning,
            Grammar = grammar,
            MeaningLabel = localizer?.Get("Llm.WordMeaningLabel") ?? "Meaning",
            GrammarLabel = localizer?.Get("Llm.GrammarLabel") ?? "Grammar",
            WordsLabel = localizer?.Get("Llm.WordsLabel") ?? "Words",
            Words = Array.AsReadOnly((memberMeanings ?? Array.Empty<WordMeaningView>()).Select(word =>
                new PhrasePopupWordViewModel(
                    word.Headword,
                    Kana.ToHiragana(word.Reading),
                    PitchText(word, localizer),
                    word.Pos,
                    word.Meaning)).ToArray()),
        };

    public static PhrasePopupViewModel ForWord(
        WordMeaningView word, IStringLocalizer? localizer = null, bool showMeaning = true)
        => new()
        {
            Heading = word.Headword,
            Reading = Kana.ToHiragana(word.Reading),
            Pos = showMeaning ? word.Pos : "",
            Pitch = PitchText(word, localizer),
            Meaning = showMeaning ? word.Meaning : "",
            Grammar = showMeaning ? word.Grammar : "",
            PosLabel = localizer?.Get("Llm.WordPosLabel") ?? "Pos",
            PitchLabel = string.IsNullOrEmpty(word.PitchAccentText) ? "" : localizer?.Get("Llm.WordPitchLabel") ?? "Pitch",
            MeaningLabel = localizer?.Get("Llm.WordMeaningLabel") ?? "Meaning",
            GrammarLabel = localizer?.Get("Llm.GrammarLabel") ?? "Grammar",
            NoMeaningHint = showMeaning ? "" : localizer?.Get("Llm.WordNoMeaning") ?? "no meaning",
        };

    private static string PitchText(WordMeaningView word, IStringLocalizer? localizer)
        => string.IsNullOrEmpty(word.PitchAccentText)
            ? localizer?.Get("Llm.WordPitchUnknown") ?? "pitch unknown"
            : word.PitchAccentText;
}

internal sealed record PhrasePopupWordViewModel(string Headword, string Reading, string Pitch, string Pos, string Meaning);

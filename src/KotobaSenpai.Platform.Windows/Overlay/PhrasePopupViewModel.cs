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
    public string Romanization { get; private init; } = "";
    public string Pronunciation => PronunciationText(Reading, Romanization);
    public string PitchMarker { get; private init; } = "";
    public string Pos { get; private init; } = "";
    public string Pitch { get; private init; } = "";
    public string Meaning { get; private init; } = "";
    public string Grammar { get; private init; } = "";
    public string NoMeaningHint { get; private init; } = "";
    public string MeaningLabel { get; private init; } = "Meaning";
    public string GrammarLabel { get; private init; } = "Grammar";
    public string WordsLabel { get; private init; } = "Words";
    public IReadOnlyList<PhrasePopupWordViewModel> Words { get; private init; } = Array.Empty<PhrasePopupWordViewModel>();

    public static PhrasePopupViewModel ForPhrase(
        string label, string meaning, string grammar,
        IReadOnlyList<WordMeaningView>? memberMeanings = null, IStringLocalizer? localizer = null, string reading = "")
    {
        var hiragana = Kana.ToHiragana(reading);
        return new()
        {
            Heading = label,
            Reading = hiragana,
            Romanization = Kana.ToRomaji(hiragana),
            Meaning = meaning,
            Grammar = grammar,
            MeaningLabel = localizer?.Get("Llm.WordMeaningLabel") ?? "Meaning",
            GrammarLabel = localizer?.Get("Llm.GrammarLabel") ?? "Grammar",
            WordsLabel = localizer?.Get("Llm.WordsLabel") ?? "Words",
            Words = Array.AsReadOnly((memberMeanings ?? Array.Empty<WordMeaningView>())
                .Select(word => CreateWord(word, localizer)).ToArray()),
        };
    }

    public static PhrasePopupViewModel ForWord(
        WordMeaningView word, IStringLocalizer? localizer = null, bool showMeaning = true)
    {
        var entry = CreateWord(word, localizer);
        return new()
        {
            Heading = entry.Headword,
            Reading = entry.Reading,
            Romanization = entry.Romanization,
            PitchMarker = entry.PitchMarker,
            Pos = showMeaning ? entry.Pos : "",
            Pitch = entry.Pitch,
            Meaning = showMeaning ? word.Meaning : "",
            Grammar = showMeaning ? word.Grammar : "",
            MeaningLabel = localizer?.Get("Llm.WordMeaningLabel") ?? "Meaning",
            GrammarLabel = localizer?.Get("Llm.GrammarLabel") ?? "Grammar",
            NoMeaningHint = showMeaning ? "" : localizer?.Get("Llm.WordNoMeaning") ?? "no meaning",
        };
    }

    internal static string PronunciationText(string reading, string romanization)
        => string.IsNullOrEmpty(reading) || reading.Equals(romanization, StringComparison.Ordinal)
            ? reading : $"{reading}  |  {romanization}";

    private static PhrasePopupWordViewModel CreateWord(WordMeaningView word, IStringLocalizer? localizer)
    {
        var reading = Kana.ToHiragana(word.Reading);
        var patterns = (word.PitchAccents ?? [])
            .Select(pitch => PitchAccent.CreatePattern(pitch.Reading, pitch.AccentPosition))
            .OfType<PitchAccentPattern>().ToArray();
        var marker = string.Concat(patterns.Select(pattern => CircledNumber(pattern.AccentPosition)));
        var pitchText = patterns.Length == 0
            ? localizer?.Get("Llm.WordPitchUnknown") ?? "pitch unknown"
            : $"{localizer?.Get("Llm.WordPitchLabel") ?? "Pitch"} "
                + string.Join(" | ", patterns.Select(pattern => pattern.Notation[(pattern.Notation.IndexOf(']') + 2)..]));
        return new(word.Headword, reading, Kana.ToRomaji(reading), marker, pitchText, word.Pos, word.Meaning);
    }

    private static string CircledNumber(int position) => position switch
    {
        0 => "⓪",
        >= 1 and <= 20 => char.ConvertFromUtf32(0x2460 + position - 1),
        >= 21 and <= 35 => char.ConvertFromUtf32(0x3251 + position - 21),
        >= 36 and <= 50 => char.ConvertFromUtf32(0x32B1 + position - 36),
        _ => $"({position})",
    };
}

internal sealed record PhrasePopupWordViewModel(
    string Headword, string Reading, string Romanization, string PitchMarker, string Pitch, string Pos, string Meaning)
{
    public string Pronunciation => PhrasePopupViewModel.PronunciationText(Reading, Romanization);
}

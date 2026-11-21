using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.PhraseAnalysis;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>A complete, read-only presentation snapshot for one popup display.</summary>
internal sealed class PhrasePopupViewModel
{
    private PhrasePopupViewModel() { }
    private IStringLocalizer? Localizer { get; init; }

    public SentenceTranslation? Sentence { get; private init; }
    public string OriginalText => Sentence?.SourceText ?? "";
    public string TranslationText => Sentence?.Translation ?? (Sentence is { Requested: false }
        ? Localizer?.Get("SentenceTranslation_NextRecognition") ?? "A sentence translation will be generated on the next recognition."
        : Localizer?.Get("SentenceTranslation_Unavailable") ?? "No sentence translation is available. Run recognition again to retry.");
    public string TranslationShowLabel => Localizer?.Get("SentenceTranslation_Show") ?? "Show sentence translation";
    public string TranslationHideLabel => Localizer?.Get("SentenceTranslation_Hide") ?? "Hide sentence translation";
    public string OriginalLabel => Localizer?.Get("SentenceTranslation_Original") ?? "Original";
    public string TranslationLabel => Localizer?.Get("SentenceTranslation_Reference") ?? "AI reference translation";

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
        IReadOnlyList<WordMeaningView>? memberMeanings = null, IStringLocalizer? localizer = null, string reading = "",
        SentenceTranslation? sentence = null)
    {
        var hiragana = Kana.ToHiragana(reading);
        return new()
        {
            Heading = label,
            Sentence = sentence,
            Localizer = localizer,
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
        WordMeaningView word, IStringLocalizer? localizer = null, bool showMeaning = true, SentenceTranslation? sentence = null)
    {
        var memberWords = BuildCorrectedMemberWords(word, localizer);
        var showAsComponentWords = memberWords.Length > 1
            || (memberWords.Length == 1 && !string.Equals(memberWords[0].Headword, word.Headword, StringComparison.Ordinal));
        var entry = CreateWord(word, localizer);
        return new()
        {
            Heading = entry.Headword,
            Sentence = sentence,
            Localizer = localizer,
            Reading = entry.Reading,
            Romanization = entry.Romanization,
            PitchMarker = showAsComponentWords ? "" : entry.PitchMarker,
            Pos = showMeaning ? entry.Pos : "",
            Pitch = showAsComponentWords ? "" : entry.Pitch,
            Meaning = showMeaning ? word.Meaning : "",
            Grammar = showMeaning ? word.Grammar : "",
            MeaningLabel = localizer?.Get("Llm.WordMeaningLabel") ?? "Meaning",
            GrammarLabel = localizer?.Get("Llm.GrammarLabel") ?? "Grammar",
            WordsLabel = localizer?.Get("Llm.WordsLabel") ?? "Words",
            Words = showAsComponentWords ? Array.AsReadOnly(memberWords) : Array.Empty<PhrasePopupWordViewModel>(),
            NoMeaningHint = showMeaning ? "" : localizer?.Get("Llm.WordNoMeaning") ?? "no meaning",
        };
    }

    internal static string PronunciationText(string reading, string romanization)
        => string.IsNullOrEmpty(reading) || reading.Equals(romanization, StringComparison.Ordinal)
            ? reading : $"{reading}  |  {romanization}";

    private static PhrasePopupWordViewModel CreateWord(WordMeaningView word, IStringLocalizer? localizer)
    {
        var reading = Kana.ToHiragana(word.Reading);
        var pitchLabel = localizer?.Get("Llm.WordPitchLabel") ?? "Pitch";
        if (word.SpanPitch is null && word.HasOverriddenReading)
        {
            var correctedMembers = BuildCorrectedMemberWords(word, localizer);
            if (correctedMembers.Length > 0)
            {
                var correctedMarker = string.Concat(correctedMembers.Select(member => member.PitchMarker));
                var correctedContours = correctedMembers
                    .Select(member => member.Pitch.StartsWith(pitchLabel + " ", StringComparison.Ordinal)
                        ? member.Pitch[(pitchLabel.Length + 1)..]
                        : member.Pitch)
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToArray();
                var correctedPitch = correctedContours.Length == 0
                    ? localizer?.Get("Llm.WordPitchUnknown") ?? "pitch unknown"
                    : $"{pitchLabel} {string.Join(" | ", correctedContours)}";
                return new(word.Headword, reading, Kana.ToRomaji(reading), correctedMarker, correctedPitch, word.Pos, word.Meaning);
            }
        }

        var patterns = word.SpanPitch is { Positions.Count: > 0 } spanPitch
            ? spanPitch.Positions.Select(position => PitchAccent.CreatePattern(spanPitch.Reading, position))
                .OfType<PitchAccentPattern>().ToArray()
            : (word.HasOverriddenReading ? [] : word.PitchAccents ?? [])
                .Select(pitch => PitchAccent.CreatePattern(pitch.Reading, pitch.AccentPosition))
                .OfType<PitchAccentPattern>().ToArray();
        var marker = string.Concat((word.SpanPitch is null ? patterns : patterns.Take(1))
            .Select(pattern => CircledNumber(pattern.AccentPosition)));
        var pitchText = patterns.Length == 0
            ? localizer?.Get("Llm.WordPitchUnknown") ?? "pitch unknown"
            : $"{pitchLabel} "
                + string.Join(word.SpanPitch is null ? " | " : " / ", patterns.Select(ContourText));
        return new(word.Headword, reading, Kana.ToRomaji(reading), marker, pitchText, word.Pos, word.Meaning);
    }

    private static PhrasePopupWordViewModel[] BuildCorrectedMemberWords(WordMeaningView word, IStringLocalizer? localizer)
    {
        if (word.SpanPitch is not null || !word.HasOverriddenReading)
            return [];

        var pitchLabel = localizer?.Get("Llm.WordPitchLabel") ?? "Pitch";
        var reference = word.WordPitch;
        if (word.CorrectedPitchAccents is { Count: > 0 } segments)
        {
            var members = new List<PhrasePopupWordViewModel>(segments.Count);
            foreach (var segment in segments)
            {
                if (segment.AccentPosition is not int primary)
                    continue;

                var segmentReading = Kana.ToHiragana(segment.Reading);
                var positions = reference is not null
                    && string.Equals(reference.Surface, segment.Surface, StringComparison.Ordinal)
                    && string.Equals(Kana.ToHiragana(reference.Reading), segmentReading, StringComparison.Ordinal)
                    ? reference.Positions
                    : [primary];
                var patterns = positions
                    .Select(position => PitchAccent.CreatePattern(segmentReading, position))
                    .OfType<PitchAccentPattern>()
                    .ToArray();
                if (patterns.Length == 0)
                    continue;

                members.Add(new PhrasePopupWordViewModel(
                    segment.Surface,
                    segmentReading,
                    Kana.ToRomaji(segmentReading),
                    CircledNumber(patterns[0].AccentPosition),
                    $"{pitchLabel} {string.Join(" / ", patterns.Select(ContourText))}",
                    "",
                    ""));
            }

            if (members.Count > 0)
                return members.ToArray();
        }

        if (reference is not null)
        {
            var refReading = Kana.ToHiragana(reference.Reading);
            var patterns = reference.Positions
                .Select(position => PitchAccent.CreatePattern(refReading, position))
                .OfType<PitchAccentPattern>()
                .ToArray();
            if (patterns.Length > 0)
            {
                return
                [
                    new PhrasePopupWordViewModel(
                        reference.Surface,
                        refReading,
                        Kana.ToRomaji(refReading),
                        CircledNumber(patterns[0].AccentPosition),
                        $"{pitchLabel} {string.Join(" / ", patterns.Select(ContourText))}",
                        "",
                        ""),
                ];
            }
        }

        return [];
    }

    private static string ContourText(PitchAccentPattern pattern)
        => pattern.Notation[(pattern.Notation.IndexOf(']') + 2)..];

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

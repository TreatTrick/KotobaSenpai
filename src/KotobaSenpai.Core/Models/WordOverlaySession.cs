using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// The aggregate root of a single recognition session. It guarantees the overlay belongs only to the
/// current target window and refreshes the word list by wholesale replacement.
/// </summary>
public sealed class WordOverlaySession
{
    private readonly List<GroupedWord> _words;
    private readonly List<PhraseGroupView> _phraseGroups;
    private readonly IReadOnlyDictionary<(string Surface, string Reading), WordMeaningView> _meaningByWord;
    private readonly IReadOnlySet<string>? _underlineSegmentIds;
    private readonly IReadOnlyList<NormalizedWordGeometry> _wordGeometry;
    private readonly IReadOnlyList<NormalizedPhraseGroupGeometry> _phraseGeometry;

    private WordOverlaySession(
        Guid id,
        WindowTarget target,
        IReadOnlyList<GroupedWord> words,
        IReadOnlyList<PhraseGroupView> phraseGroups,
        string? phraseWarning,
        IReadOnlyList<WordMeaningView> wordMeanings,
        IReadOnlySet<string>? underlineSegmentIds,
        IReadOnlyList<NormalizedWordGeometry> wordGeometry,
        IReadOnlyList<NormalizedPhraseGroupGeometry> phraseGeometry)
    {
        Id = id;
        Target = target;
        _words = words.ToList();
        _phraseGroups = phraseGroups.ToList();
        PhraseWarning = phraseWarning;
        _underlineSegmentIds = underlineSegmentIds;
        _wordGeometry = wordGeometry;
        _phraseGeometry = phraseGeometry;
        // ponytail: keyed by merged surface+reading — both come from the same span resolver, so this matches reliably. A word can appear multiple times (same surface+reading); keep the first rather than throwing on a duplicate.
        var meaningByWord = new Dictionary<(string Surface, string Reading), WordMeaningView>();
        foreach (var meaning in wordMeanings)
            meaningByWord.TryAdd((meaning.Headword, meaning.Reading), meaning);
        _meaningByWord = meaningByWord;
    }

    public Guid Id { get; }

    public WindowTarget Target { get; }

    public IReadOnlyList<GroupedWord> Words => _words;

    /// <summary>Validated phrase groups (with per-part screen geometry); empty when no phrase analysis ran.</summary>
    public IReadOnlyList<PhraseGroupView> PhraseGroups => _phraseGroups;

    /// <summary>A retryable warning from phrase analysis; null when there was no failure.</summary>
    public string? PhraseWarning { get; }

    /// <summary>Successful sentence segments eligible for underlines; null preserves legacy all-word behavior.</summary>
    public IReadOnlySet<string>? UnderlineSegmentIds => _underlineSegmentIds;

    /// <summary>All validated word meanings, for diagnostics and group-detail rendering.</summary>
    public IReadOnlyList<WordMeaningView> WordMeanings => _meaningByWord.Values.ToArray();

    /// <summary>Reprojects the stable recognition geometry into a current target client rectangle.</summary>
    public WordOverlaySession Reproject(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var words = _words
            .Select((word, index) => word.WithRects(_wordGeometry[index].ToScreen(target.Bounds)))
            .ToArray();
        var phraseGroups = _phraseGroups
            .Select((group, groupIndex) => group with
            {
                Parts = group.Parts
                    .Select((part, partIndex) => new PhrasePartView(
                        _phraseGeometry[groupIndex].Parts[partIndex].ToScreen(target.Bounds))
                    {
                        Surface = part.Surface,
                        Reading = part.Reading,
                        PitchAccents = part.PitchAccents,
                    })
                    .ToArray(),
            })
            .ToArray();
        return new WordOverlaySession(
            Id,
            target,
            words,
            phraseGroups,
            PhraseWarning,
            _meaningByWord.Values.ToArray(),
            _underlineSegmentIds,
            _wordGeometry,
            _phraseGeometry);
    }

    /// <summary>Looks up the LLM meaning for a local merged word (by its merged surface+reading), or null when none was returned.</summary>
    public WordMeaningView? TryGetMeaning(GroupedWord word)
        => _meaningByWord.TryGetValue((word.Surface, word.Reading), out var meaning) ? meaning : null;

    public IReadOnlyList<WordMeaningView> GetCoveredWordMeanings(PhraseGroupView group)
        => GetCoveredWordIndices(group)
            .Select(index => _words[index])
            .Select(word => TryGetMeaning(word) ?? WordMeaningView.FromWord(word))
            .ToArray();

    /// <summary>Returns the indices of the local merged words whose bounds overlap a phrase group's part rects (group membership signal).</summary>
    public IReadOnlyList<int> GetCoveredWordIndices(PhraseGroupView group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var indices = new HashSet<int>();
        foreach (var part in group.Parts)
            foreach (var rect in part.Rects)
                for (int i = 0; i < _words.Count; i++)
                    if (Intersects(_words[i].Bounds, rect))
                        indices.Add(i);
        return indices.ToArray();
    }

    private static bool Intersects(ScreenRect a, ScreenRect b)
        => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;

    /// <summary>One underline at the bottom inside of each word rect (a cross-line word has one per line); replaced wholesale on refresh, leaving no residue.</summary>
    public IReadOnlyList<OverlayLine> Lines => _words
        .Where(ShouldUnderline)
        .SelectMany(word => word.Rects.Select(rect => new OverlayLine(
            rect.X,
            Math.Max(rect.Y, rect.Bottom - 2),
            rect.Width)))
        .ToArray();

    /// <summary>Starts a new session: filters zero-width words and validates the target-window invariants.</summary>
    public static WordOverlaySession Start(
        WindowTarget target,
        IEnumerable<GroupedWord> words,
        IEnumerable<PhraseGroupView>? phraseGroups = null,
        string? phraseWarning = null,
        IEnumerable<WordMeaningView>? wordMeanings = null,
        IEnumerable<string>? underlineSegmentIds = null)
    {
        ArgumentNullException.ThrowIfNull(words);

        var validWords = words.Where(word => word.Bounds.Width > 0).ToArray();
        if (target is null)
            throw new BusinessRuleValidationException(
                ErrorCodes.OverlayTargetNotSpecified,
                "Overlay session target must be specified.");
        var wordGeometry = validWords
            .Select(word => new NormalizedWordGeometry(
                word.Rects.Select(rect => NormalizedRect.FromScreen(rect, target.Bounds))))
            .ToArray();
        var groups = (phraseGroups ?? Array.Empty<PhraseGroupView>()).ToArray();
        var phraseGeometry = groups
            .Select(group => new NormalizedPhraseGroupGeometry(
                group.Parts
                    .Select(part => new NormalizedPhrasePartGeometry(
                        part.Rects.Select(rect => NormalizedRect.FromScreen(rect, target.Bounds))))))
            .ToArray();
        return new WordOverlaySession(
            Guid.NewGuid(),
            target,
            validWords,
            groups,
            phraseWarning,
            (wordMeanings ?? Array.Empty<WordMeaningView>()).ToArray(),
            underlineSegmentIds is null
                ? null
                : new HashSet<string>(underlineSegmentIds, StringComparer.Ordinal),
            wordGeometry,
            phraseGeometry);
    }

    /// <summary>Returns whether a word's underline should be included in this session.</summary>
    public bool ShouldUnderline(GroupedWord word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return _underlineSegmentIds is null
            || (word.SegmentId is not null && _underlineSegmentIds.Contains(word.SegmentId));
    }
}

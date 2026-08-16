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

    private WordOverlaySession(
        Guid id,
        WindowTarget target,
        IReadOnlyList<GroupedWord> words,
        IReadOnlyList<PhraseGroupView> phraseGroups,
        string? phraseWarning,
        IReadOnlyList<WordMeaningView> wordMeanings)
    {
        Id = id;
        Target = target;
        _words = words.ToList();
        _phraseGroups = phraseGroups.ToList();
        PhraseWarning = phraseWarning;
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

    /// <summary>All validated word meanings, for diagnostics and group-detail rendering.</summary>
    public IReadOnlyList<WordMeaningView> WordMeanings => _meaningByWord.Values.ToArray();

    /// <summary>Looks up the LLM meaning for a local merged word (by its merged surface+reading), or null when none was returned.</summary>
    public WordMeaningView? TryGetMeaning(GroupedWord word)
        => _meaningByWord.TryGetValue((word.Surface, word.Reading), out var meaning) ? meaning : null;

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

    /// <summary>One underline at the bottom inside of each word box; replaced wholesale on refresh, leaving no residue.</summary>
    public IReadOnlyList<OverlayLine> Lines => _words
        .Select(word => new OverlayLine(
            word.Bounds.X,
            Math.Max(word.Bounds.Y, word.Bounds.Bottom - 2),
            word.Bounds.Width))
        .ToArray();

    /// <summary>Starts a new session: filters zero-width words and validates the target-window invariants.</summary>
    public static WordOverlaySession Start(
        WindowTarget target,
        IEnumerable<GroupedWord> words,
        IEnumerable<PhraseGroupView>? phraseGroups = null,
        string? phraseWarning = null,
        IEnumerable<WordMeaningView>? wordMeanings = null)
    {
        ArgumentNullException.ThrowIfNull(words);

        var validWords = words.Where(word => word.Bounds.Width > 0).ToArray();
        if (target is null)
            throw new BusinessRuleValidationException(
                ErrorCodes.OverlayTargetNotSpecified,
                "Overlay session target must be specified.");
        return new WordOverlaySession(
            Guid.NewGuid(),
            target,
            validWords,
            (phraseGroups ?? Array.Empty<PhraseGroupView>()).ToArray(),
            phraseWarning,
            (wordMeanings ?? Array.Empty<WordMeaningView>()).ToArray());
    }
}

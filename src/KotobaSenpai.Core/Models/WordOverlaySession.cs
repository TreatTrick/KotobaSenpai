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

    private WordOverlaySession(
        Guid id,
        WindowTarget target,
        IReadOnlyList<GroupedWord> words,
        IReadOnlyList<PhraseGroupView> phraseGroups,
        string? phraseWarning)
    {
        Id = id;
        Target = target;
        _words = words.ToList();
        _phraseGroups = phraseGroups.ToList();
        PhraseWarning = phraseWarning;
    }

    public Guid Id { get; }

    public WindowTarget Target { get; }

    public IReadOnlyList<GroupedWord> Words => _words;

    /// <summary>Validated phrase groups (with per-part screen geometry); empty when no phrase analysis ran.</summary>
    public IReadOnlyList<PhraseGroupView> PhraseGroups => _phraseGroups;

    /// <summary>A retryable warning from phrase analysis; null when there was no failure.</summary>
    public string? PhraseWarning { get; }

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
        string? phraseWarning = null)
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
            phraseWarning);
    }
}

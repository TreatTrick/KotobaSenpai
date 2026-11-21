using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Overlay;

/// <summary>
/// The aggregate root of a single recognition session. It guarantees the overlay belongs only to the
/// current target window and refreshes the word list by wholesale replacement.
/// </summary>
public sealed class WordOverlaySession
{
    private readonly List<GroupedWord> _words;
    private readonly List<PhraseGroupView> _phraseGroups;
    private readonly IReadOnlyList<WordMeaningView> _wordMeanings;
    private readonly Dictionary<(string SegmentId, int SpanIndex), WordMeaningView> _meaningBySpan = new();
    private readonly Dictionary<(string Surface, string Reading), WordMeaningView> _legacyMeaningByWord = new();
    private readonly IReadOnlySet<string>? _underlineSegmentIds;
    private readonly IReadOnlyList<NormalizedWordGeometry> _wordGeometry;
    private readonly IReadOnlyList<NormalizedPhraseGroupGeometry> _phraseGeometry;
    private readonly IReadOnlyDictionary<string, SentenceTranslation> _sentences;

    private WordOverlaySession(
        Guid id,
        WindowTarget target,
        IReadOnlyList<GroupedWord> words,
        IReadOnlyList<PhraseGroupView> phraseGroups,
        string? phraseWarning,
        IReadOnlyList<WordMeaningView> wordMeanings,
        IReadOnlySet<string>? underlineSegmentIds,
        IReadOnlyList<NormalizedWordGeometry> wordGeometry,
        IReadOnlyList<NormalizedPhraseGroupGeometry> phraseGeometry,
        IEnumerable<SentenceTranslation> sentences)
    {
        Id = id;
        Target = target;
        _words = words.ToList();
        _phraseGroups = phraseGroups.ToList();
        PhraseWarning = phraseWarning;
        _underlineSegmentIds = underlineSegmentIds;
        _wordGeometry = wordGeometry;
        _phraseGeometry = phraseGeometry;
        _sentences = sentences.ToDictionary(sentence => sentence.SegmentId, StringComparer.Ordinal);
        var wordsBySpan = new Dictionary<(string, int), GroupedWord>();
        foreach (var word in _words)
        {
            if (word.SegmentId is { } segmentId && word.SegmentSpanIndex is int spanIndex)
                wordsBySpan.TryAdd((segmentId, spanIndex), word);
        }
        _wordMeanings = wordMeanings.Select(meaning =>
            meaning.SegmentId is { } segmentId && meaning.SpanIndex is int spanIndex
                && wordsBySpan.TryGetValue((segmentId, spanIndex), out var word)
                ? WithLocalPitch(word, meaning)
                : meaning).ToArray();
        foreach (var meaning in _wordMeanings)
        {
            if (meaning.SegmentId is { } segmentId && meaning.SpanIndex is { } spanIndex)
                _meaningBySpan.TryAdd((segmentId, spanIndex), meaning);
            else
                _legacyMeaningByWord.TryAdd((meaning.Headword, meaning.Reading), meaning);
        }
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
    public IReadOnlyList<WordMeaningView> WordMeanings => _wordMeanings;

    public SentenceTranslation? GetSentenceTranslation(string? segmentId)
        => segmentId is not null && _sentences.TryGetValue(segmentId, out var sentence) ? sentence : null;

    /// <summary>Reprojects the stable recognition geometry into a current target client rectangle.</summary>
    public WordOverlaySession Reproject(WindowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var words = _words
            .Select((word, index) =>
            {
                var geometry = _wordGeometry[index];
                var rects = geometry.ToScreen(target.Bounds);
                if (geometry.SourceTokenLines.Count != word.SourceTokens.Count)
                    return word.WithRects(rects);

                var sourceTokenLines = geometry.SourceTokenLines
                    .Select(lines => (IReadOnlyList<SourceTokenLineGeometry>)lines
                        .Select(line => new SourceTokenLineGeometry(
                            line.LineIndex,
                            line.SurfaceOffset,
                            line.SurfaceLength,
                            line.Bounds.ToScreen(target.Bounds)))
                        .ToArray())
                    .ToArray();
                return word.WithRects(rects, sourceTokenLines);
            })
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
            _wordMeanings,
            _underlineSegmentIds,
            _wordGeometry,
            _phraseGeometry,
            _sentences.Values);
    }

    /// <summary>Finds the meaning for this occurrence, falling back to surface and reading for legacy meanings.</summary>
    public WordMeaningView? TryGetMeaning(GroupedWord word)
    {
        if (word.SegmentId is { } segmentId && word.SegmentSpanIndex is { } spanIndex
            && _meaningBySpan.TryGetValue((segmentId, spanIndex), out var meaning) && meaning.Headword == word.Surface)
            return meaning;
        return _legacyMeaningByWord.TryGetValue((word.Surface, word.Reading), out var legacyMeaning)
            ? WithLocalPitch(word, legacyMeaning)
            : null;
    }

    private static WordMeaningView WithLocalPitch(GroupedWord word, WordMeaningView meaning)
        => meaning.Headword == word.Surface && meaning.Reading == word.Reading
            ? meaning with
            {
                SpanPitch = word.SpanPitch ?? meaning.SpanPitch,
                WordPitch = word.SpanPitch is null ? word.WordPitch ?? meaning.WordPitch : null,
                HasOverriddenReading = word.HasOverriddenReading || meaning.HasOverriddenReading,
            }
            : meaning;

    public WordMeaningView GetMeaningOrDefault(GroupedWord word)
        => TryGetMeaning(word) ?? FromWord(word);

    public IReadOnlyList<WordMeaningView> GetCoveredWordMeanings(PhraseGroupView group)
        => GetCoveredWordIndices(group)
            .Select(index => _words[index])
            .Select(GetMeaningOrDefault)
            .ToArray();

    /// <summary>Returns the local merged words whose per-line rects overlap a phrase group's part rects.</summary>
    public IReadOnlyList<int> GetCoveredWordIndices(PhraseGroupView group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var indices = new HashSet<int>();
        foreach (var part in group.Parts)
            foreach (var rect in part.Rects)
                for (int i = 0; i < _words.Count; i++)
                    if (_words[i].Rects.Any(wordRect => Intersects(wordRect, rect)))
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
        IEnumerable<string>? underlineSegmentIds = null,
        IEnumerable<SentenceTranslation>? sentences = null)
    {
        ArgumentNullException.ThrowIfNull(words);

        var validWords = words.Where(word => word.Bounds.Width > 0).ToArray();
        if (target is null)
            throw new BusinessRuleValidationException(
                ErrorCodes.OverlayTargetNotSpecified,
                "Overlay session target must be specified.");
        var wordGeometry = validWords
            .Select(word => new NormalizedWordGeometry(
                word.Rects.Select(rect => NormalizedRect.FromScreen(rect, target.Bounds)),
                word.SourceTokenLines
                    .Select(lines => (IReadOnlyList<NormalizedSourceTokenLineGeometry>)lines
                        .Select(line => new NormalizedSourceTokenLineGeometry(
                            line.LineIndex,
                            line.SurfaceOffset,
                            line.SurfaceLength,
                            NormalizedRect.FromScreen(line.Bounds, target.Bounds)))
                        .ToArray())
                    .ToArray()))
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
            phraseGeometry,
            sentences ?? Array.Empty<SentenceTranslation>());
    }

    /// <summary>Returns whether a word's underline should be included in this session.</summary>
    public bool ShouldUnderline(GroupedWord word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return _underlineSegmentIds is null
            || (word.SegmentId is not null && _underlineSegmentIds.Contains(word.SegmentId));
    }

    private static WordMeaningView FromWord(GroupedWord word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return WithLocalPitch(word,
            new WordMeaningView(word.Surface, word.Reading, string.Empty, string.Empty, string.Empty, word.PitchAccents)
            {
                SegmentId = word.SegmentId,
                SpanIndex = word.SegmentSpanIndex,
            });
    }
}

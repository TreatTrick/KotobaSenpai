using System.Windows.Threading;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Platform.Windows.Overlay;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Owns polling and the coupled word, phrase and popup hover states.</summary>
internal sealed class OverlayHoverController
{
    private readonly OverlayAnnotationLayer _annotations;
    private readonly PhrasePopup _phrasePopup;
    private readonly DispatcherTimer _hoverTimer;
    private WordOverlaySession? _session;
    private int _hoverIndex = -1;
    private int _hoveredGroup = -1;
    private IReadOnlyList<int> _hoveredGroupLines = Array.Empty<int>();

    public OverlayHoverController(OverlayAnnotationLayer annotations, PhrasePopup popup)
    {
        _annotations = annotations;
        _phrasePopup = popup;
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _hoverTimer.Tick += (_, _) => PollHover();
    }

    public void Start(WordOverlaySession session)
    {
        _session = session;
        _hoverTimer.Start();
    }

    public void ClearSession()
    {
        Stop();
        _session = null;
    }

    public void Stop()
    {
        _hoverTimer.Stop();
        ClearHover();
    }

    private void ClearHover()
    {
        _hoverIndex = -1;
        _hoveredGroup = -1;
        _hoveredGroupLines = Array.Empty<int>();
        DismissPopup();
    }

    private void DismissPopup()
    {
        _phrasePopup.HidePopup();
        _annotations.ClearHighlight();
    }

    /// <summary>
    /// Polls the cursor position for hit testing (the window is click-through and receives no mouse events of its own).
    /// Tests phrase groups first (overlaps resolved by fewer tokens, then provider order), highlighting the underlines of
    /// the group's member words; otherwise falls back to the local-word hot zone. Retains the active popup
    /// while approaching it or staying within its exit margin, before resolving a different source.
    /// </summary>
    private void PollHover()
    {
        if (OverlayWindowNativeHost.TryGetCursorPosition(out var x, out var y))
            PollHover(x, y);
    }

    internal void PollHover(int cursorX, int cursorY)
    {
        var session = _session;
        if (session is null || (session.Words.Count == 0 && session.PhraseGroups.Count == 0))
            return;

        var groupHit = PhraseHoverResolver.Resolve(session.PhraseGroups, cursorX, cursorY);
        var hit = groupHit < 0 ? WordHoverResolver.Resolve(session.Words, cursorX, cursorY) : -1;
        if (_phrasePopup.IsVisible)
        {
            var isOverSource = _hoveredGroup >= 0
                ? groupHit == _hoveredGroup
                : hit >= 0 && hit == _hoverIndex;
            if (_phrasePopup.ShouldRemainOpen(cursorX, cursorY, isOverSource))
                return;
            if (isOverSource)
            {
                // Keep the source identity until it is left, so dismissal does not immediately reopen it.
                DismissPopup();
                return;
            }
            ClearHover();
        }
        if (groupHit != _hoveredGroup)
        {
            _annotations.SetHighlight(_hoveredGroupLines, false);
            _hoveredGroupLines = Array.Empty<int>();
            _hoveredGroup = groupHit;
            if (groupHit >= 0)
            {
                _hoveredGroupLines = OverlayAnnotationLayer.GetLineIndicesForWords(
                    session.GetCoveredWordIndices(session.PhraseGroups[groupHit]),
                    _annotations.LineOwners);
                _annotations.SetHighlight(_hoveredGroupLines, true);
                ShowPhrasePopup(session.PhraseGroups[groupHit], cursorX, cursorY);
            }
            else
            {
                _phrasePopup.HidePopup();
            }
        }

        if (groupHit >= 0)
        {
            // Suspend local-word hover while a group is hit, to keep the two popup states from fighting.
            UnhighlightWord(_hoverIndex);
            _hoverIndex = -1;
            _phrasePopup.ShouldRemainOpen(cursorX, cursorY, isOverSource: true);
            return;
        }

        if (hit == _hoverIndex)
            return;
        UnhighlightWord(_hoverIndex);
        _hoverIndex = hit;
        if (hit >= 0)
            _annotations.SetHighlight(WordLineIndices(hit), true);
        UpdatePopup(hit, cursorX, cursorY);
        _phrasePopup.ShouldRemainOpen(cursorX, cursorY, isOverSource: true);
    }

    private void UnhighlightWord(int wordIndex)
    {
        if (wordIndex < 0)
            return;
        _annotations.SetHighlight(WordLineIndices(wordIndex), false);
    }

    private IEnumerable<int> WordLineIndices(int wordIndex)
        => Enumerable.Range(0, _annotations.LineOwners.Count).Where(li => _annotations.LineOwners[li] == wordIndex);

    private static bool Contains(ScreenRect rect, int x, int y)
        => x >= rect.X && x <= rect.Right && y >= rect.Y && y <= rect.Bottom;

    private void ShowPhrasePopup(PhraseGroupView group, int cursorX, int cursorY)
    {
        var anchor = group.Parts.SelectMany(part => part.Rects).FirstOrDefault(rect => Contains(rect, cursorX, cursorY));
        if (anchor == default)
        {
            _phrasePopup.HidePopup();
            return;
        }
        var session = _session;
        var memberMeanings = session is null
            ? Array.Empty<WordMeaningView>()
            : session.GetCoveredWordMeanings(group);
        var reading = string.Join(" ", group.Parts.Select(part => part.Reading).Where(value => !string.IsNullOrWhiteSpace(value)));
        _phrasePopup.ShowResult(group.Label, group.Meaning, group.Grammar, anchor, memberMeanings, reading,
            session?.GetSentenceTranslation(group.SegmentId));
    }

    /// <summary>Shows the hovered word's LLM meaning popup, or headword + reading + "no meaning" when the provider returned none.</summary>
    private void UpdatePopup(int hit, int cursorX, int cursorY)
    {
        var session = _session;
        if (session is null || hit < 0 || hit >= session.Words.Count)
        {
            _phrasePopup.HidePopup();
            return;
        }

        var word = session.Words[hit];
        var anchor = word.Rects.First(rect => Contains(rect, cursorX, cursorY));
        var meaning = session.TryGetMeaning(word);
        if (meaning is not null)
            _phrasePopup.ShowWordMeaning(meaning, anchor, session.GetSentenceTranslation(word.SegmentId));
        else
            _phrasePopup.ShowWordWithoutMeaning(session.GetMeaningOrDefault(word), anchor, session.GetSentenceTranslation(word.SegmentId));
    }
}

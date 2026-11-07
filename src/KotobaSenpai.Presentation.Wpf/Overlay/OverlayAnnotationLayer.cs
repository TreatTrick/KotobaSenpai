using System.Windows;
using System.Windows.Controls;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.WordGrouping;

namespace KotobaSenpai.Presentation.Wpf.Overlay;

/// <summary>Retains and lays out annotations independently of native window and popup lifetimes.</summary>
internal sealed class OverlayAnnotationLayer
{
    private OverlayAnnotationStyle _annotationStyle = new(OverlayAppearanceSettings.Default);

    private readonly Canvas _canvas = new();
    private readonly List<OverlayAnnotation> _lineElements = new();
    private readonly List<int> _lineOwner = new(); // parallel to _lineElements: the word index each line belongs to
    private readonly List<RenderedWord> _renderedWords = new();
    private sealed record RenderedWord(GroupedWord Word, bool Underlined, double Scale, double FontScale,
        IReadOnlyList<OverlayAnnotation> Elements, IReadOnlyList<OverlayAnnotation> Underlines);

    private WordOverlaySession? _session;
    public Canvas Canvas => _canvas;
    public int UnderlineCount => _lineElements.Count;
    public IReadOnlyList<int> LineOwners => _lineOwner;

    public void Render(WordOverlaySession session, double scale, double fontScale)
    {
        _session = session;
        var previousWords = _renderedWords.ToList();
        _renderedWords.Clear();
        _lineElements.Clear();
        _lineOwner.Clear();

        // Retain unchanged annotations so adding a sentence does not remove and recreate earlier text.
        for (int wordIndex = 0; wordIndex < session.Words.Count; wordIndex++)
        {
            var word = session.Words[wordIndex];
            var underlined = session.ShouldUnderline(word);
            var previousIndex = previousWords.FindIndex(previous => Matches(previous, word, underlined, scale, fontScale));
            if (previousIndex >= 0)
            {
                var previous = previousWords[previousIndex];
                previousWords.RemoveAt(previousIndex);
                _renderedWords.Add(previous);
                _lineElements.AddRange(previous.Underlines);
                _lineOwner.AddRange(previous.Underlines.Select(_ => wordIndex));
                continue;
            }
            var firstElement = _canvas.Children.Count;
            var firstUnderline = _lineElements.Count;
            if (session.ShouldUnderline(word))
            {
                foreach (var rect in word.Rects)
                {
                    var element = OverlayAnnotation.CreatePill(rect.Width / scale, 2.0 / scale,
                        OverlayColorRole.Underline, _annotationStyle);
                    Canvas.SetLeft(element, (rect.X - session.Target.Bounds.X) / scale);
                    Canvas.SetTop(element, (Math.Max(rect.Y, rect.Bottom - 2) - session.Target.Bounds.Y) / scale);
                    _canvas.Children.Add(element);
                    _lineElements.Add(element);
                    _lineOwner.Add(wordIndex);
                }
            }

            if (!string.IsNullOrEmpty(word.Reading))
                AddPitchAnnotations(word, scale, fontScale);
            _renderedWords.Add(new RenderedWord(word, underlined, scale, fontScale,
                _canvas.Children.Cast<OverlayAnnotation>().Skip(firstElement).ToArray(),
                _lineElements.Skip(firstUnderline).ToArray()));
        }
        foreach (var obsolete in previousWords)
            foreach (var element in obsolete.Elements) _canvas.Children.Remove(element);
    }

    public void ApplyAppearance(OverlayAppearance appearance)
    {
        if (_annotationStyle.Appearance == appearance)
            return;
        _annotationStyle = new OverlayAnnotationStyle(appearance);
        foreach (var annotation in _canvas.Children.OfType<OverlayAnnotation>())
            annotation.ApplyStyle(_annotationStyle);
    }

    public void Clear()
    {
        _session = null;
        _canvas.Children.Clear();
        _renderedWords.Clear();
        _lineElements.Clear();
        _lineOwner.Clear();
    }

    public void SetHighlight(IEnumerable<int> lineIndices, bool highlighted)
    {
        foreach (var index in lineIndices)
            if (index >= 0 && index < _lineElements.Count)
                _lineElements[index].Role = highlighted ? OverlayColorRole.Hover : OverlayColorRole.Underline;
    }

    public void ClearHighlight()
    {
        foreach (var element in _lineElements)
            element.Role = OverlayColorRole.Underline;
    }

    internal static IReadOnlyList<int> GetLineIndicesForWords(
        IEnumerable<int> wordIndices,
        IReadOnlyList<int> lineOwners)
    {
        ArgumentNullException.ThrowIfNull(wordIndices);
        ArgumentNullException.ThrowIfNull(lineOwners);

        var words = wordIndices.ToHashSet();
        return Enumerable.Range(0, lineOwners.Count)
            .Where(lineIndex => words.Contains(lineOwners[lineIndex]))
            .ToArray();
    }

    private static bool Matches(RenderedWord previous, GroupedWord word, bool underlined, double scale, double fontScale)
        => previous.Underlined == underlined && previous.Scale == scale && previous.FontScale == fontScale
            && previous.Word.Surface == word.Surface && previous.Word.Reading == word.Reading
            && previous.Word.Rects.SequenceEqual(word.Rects)
            && previous.Word.SourceTokens.Select(token => (token.Surface, token.Reading, token.AType))
                .SequenceEqual(word.SourceTokens.Select(token => (token.Surface, token.Reading, token.AType)))
            && previous.Word.SourceTokenLines.Count == word.SourceTokenLines.Count
            && previous.Word.SourceTokenLines.Zip(word.SourceTokenLines).All(pair => pair.First.SequenceEqual(pair.Second));

    /// <summary>
    /// Adds furigana and pitch markers for each source token. Surface kana that already has a matching reading
    /// is represented by dots; only reading assigned to a kanji segment is rendered as furigana text.
    /// </summary>
    private void AddPitchAnnotations(GroupedWord word, double scale, double fontScale)
    {
        if (word.PitchAccents.Count == 0)
        {
            AddMonochromeFurigana(word.Surface, word.Reading, 0, word, scale, fontScale);
            return;
        }

        foreach (var (segment, sourceTokenIndex) in word.PitchAccents.Select((segment, index) => (segment, index)))
        {
            var pattern = PitchAccent.CreatePattern(Kana.ToHiragana(segment.Reading), segment.AccentPosition);
            if (pattern is null)
            {
                AddMonochromeFurigana(segment.Surface, segment.Reading, sourceTokenIndex, word, scale, fontScale);
                continue;
            }

            var furiganaSegments = FuriganaSettings.GetFuriganaSegments(segment.Surface, segment.Reading);
            var moraOffset = 0;
            for (var furiganaSegmentIndex = 0; furiganaSegmentIndex < furiganaSegments.Count; furiganaSegmentIndex++)
            {
                var furiganaSegment = furiganaSegments[furiganaSegmentIndex];
                var surface = segment.Surface.Substring(furiganaSegment.SurfaceStart, furiganaSegment.SurfaceLength);
                var reading = furiganaSegment.IsKana ? surface : furiganaSegment.Reading;
                var moraCount = PitchAccent.SplitMorae(Kana.ToHiragana(reading)).Count;
                if (moraCount == 0)
                    continue;

                if (furiganaSegment.IsKana)
                {
                    AddPitchDots(
                        segment,
                        furiganaSegment,
                        pattern,
                        moraOffset,
                        moraCount,
                        sourceTokenIndex,
                        word,
                        scale,
                        fontScale);
                }
                else if (FuriganaSettings.ContainsKanji(surface) && !string.IsNullOrEmpty(furiganaSegment.Reading))
                {
                    AddColoredFurigana(
                        furiganaSegment,
                        pattern,
                        moraOffset,
                        moraCount,
                        sourceTokenIndex,
                        word,
                        scale,
                        fontScale,
                        addHeibanMarker: furiganaSegmentIndex == furiganaSegments.Count - 1);
                }

                moraOffset += moraCount;
            }

            // A malformed or ambiguous distribution can produce no kana segment; the colored furigana path
            // adds the heiban marker for an all-kanji token, while kana segments add it after their final dots.
        }
    }

    private void AddMonochromeFurigana(
        string surface,
        string reading,
        int sourceTokenIndex,
        GroupedWord word,
        double scale,
        double fontScale)
    {
        foreach (var segment in FuriganaSettings.GetFuriganaSegments(surface, reading))
        {
            var surfaceText = surface.Substring(segment.SurfaceStart, segment.SurfaceLength);
            if (segment.IsKana || !FuriganaSettings.ContainsKanji(surfaceText) || string.IsNullOrEmpty(segment.Reading))
                continue;

            var morae = PitchAccent.SplitMorae(Kana.ToHiragana(segment.Reading));
            foreach (var part in FuriganaLayout.GetParts(word, sourceTokenIndex, segment.SurfaceStart, segment.SurfaceLength, morae.Count))
            {
                var fontSize = (part.Bounds.Height / scale) * fontScale;
                var text = OverlayAnnotation.CreateText(string.Concat(morae.Skip(part.MoraStart).Take(part.MoraCount)),
                    fontSize, OverlayColorRole.Furigana, _annotationStyle, scale);
                AddTextCentered(text, part.Bounds, scale, fontSize);
            }
        }
    }

    private void AddColoredFurigana(
        FuriganaSegment furiganaSegment,
        PitchAccentPattern pattern,
        int moraOffset,
        int moraCount,
        int sourceTokenIndex,
        GroupedWord word,
        double scale,
        double fontScale,
        bool addHeibanMarker)
    {
        if (string.IsNullOrEmpty(furiganaSegment.Reading) || moraCount <= 0)
            return;

        foreach (var part in FuriganaLayout.GetParts(word, sourceTokenIndex,
            furiganaSegment.SurfaceStart, furiganaSegment.SurfaceLength, moraCount))
        {
            var bounds = part.Bounds;
            var fontSize = (bounds.Height / scale) * fontScale;
            var readingStart = moraOffset + part.MoraStart;
            var fontMorae = pattern.Morae.Skip(readingStart).Take(part.MoraCount).ToArray();
            var highMorae = pattern.HighMorae.Skip(readingStart).Take(fontMorae.Length).ToArray();
            var textBlocks = new List<OverlayAnnotation>(fontMorae.Length);
            var totalWidth = 0d;
            for (var i = 0; i < fontMorae.Length; i++)
            {
                var block = OverlayAnnotation.CreateText(fontMorae[i], fontSize,
                    highMorae[i] ? OverlayColorRole.High : OverlayColorRole.Low, _annotationStyle, scale);
                block.Measure(new Size(double.PositiveInfinity, fontSize));
                textBlocks.Add(block);
                totalWidth += block.DesiredSize.Width;
            }

            var cursor = (bounds.X - _session!.Target.Bounds.X) / scale
                + (bounds.Width / scale - totalWidth) / 2;
            var top = TopDip(bounds, scale, fontSize);
            for (var i = 0; i < textBlocks.Count; i++)
            {
                var block = textBlocks[i];
                _canvas.Children.Add(block);
                Canvas.SetLeft(block, Math.Max(0, cursor));
                Canvas.SetTop(block, top);
                cursor += block.DesiredSize.Width;
            }

            if (addHeibanMarker && part.MoraStart + part.MoraCount == moraCount
                && pattern.AccentPosition == 0 && pattern.Morae.Count > 1)
                AddHeibanMarker(cursor, top + fontSize / 2, fontSize);
        }
    }

    private void AddPitchDots(
        PitchAccentSummary segment,
        FuriganaSegment furiganaSegment,
        PitchAccentPattern pattern,
        int moraOffset,
        int moraCount,
        int sourceTokenIndex,
        GroupedWord word,
        double scale,
        double fontScale)
    {
        var kanaSurface = segment.Surface.Substring(furiganaSegment.SurfaceStart, furiganaSegment.SurfaceLength);
        var kanaMorae = PitchAccent.SplitMorae(kanaSurface);
        var surfaceOffset = furiganaSegment.SurfaceStart;
        for (var i = 0; i < Math.Min(kanaMorae.Count, moraCount); i++)
        {
            foreach (var part in FuriganaLayout.GetParts(word, sourceTokenIndex, surfaceOffset, kanaMorae[i].Length, 1))
            {
                var bounds = part.Bounds;
                var fontSize = (bounds.Height / scale) * fontScale;
                var dotRadius = Math.Max(2d, fontSize / 4d);
                var centerY = Math.Max(dotRadius, (bounds.Y - _session!.Target.Bounds.Y) / scale - dotRadius - 2);
                var centerX = (bounds.X - _session.Target.Bounds.X + bounds.Width / 2d) / scale;
                var dot = OverlayAnnotation.CreatePill(dotRadius * 2, dotRadius * 2,
                    pattern.HighMorae[moraOffset + i] ? OverlayColorRole.High : OverlayColorRole.Low, _annotationStyle);
                _canvas.Children.Add(dot);
                Canvas.SetLeft(dot, centerX - dotRadius);
                Canvas.SetTop(dot, centerY - dotRadius);

                if (segment.AccentPosition == 0 && moraOffset + i + 1 == pattern.Morae.Count && pattern.Morae.Count > 1)
                    AddHeibanMarker((bounds.Right - _session.Target.Bounds.X) / scale, centerY, fontSize);
            }
            surfaceOffset += kanaMorae[i].Length;
        }
    }

    private void AddTextCentered(OverlayAnnotation text, ScreenRect bounds, double scale, double fontSize)
    {
        _canvas.Children.Add(text);
        text.Measure(new Size(double.PositiveInfinity, fontSize));
        var center = (bounds.X - _session!.Target.Bounds.X + bounds.Width / 2d) / scale;
        Canvas.SetLeft(text, Math.Max(0, center - text.DesiredSize.Width / 2));
        Canvas.SetTop(text, TopDip(bounds, scale, fontSize));
    }

    private void AddHeibanMarker(double x, double y, double fontSize)
    {
        var marker = OverlayAnnotation.CreatePill(Math.Max(3, fontSize / 3), Math.Max(1, fontSize / 8),
            OverlayColorRole.Heiban, _annotationStyle);
        _canvas.Children.Add(marker);
        Canvas.SetLeft(marker, x + 1);
        Canvas.SetTop(marker, y);
    }

    private double TopDip(ScreenRect bounds, double scale, double fontSize)
    {
        var topDip = (bounds.Y - _session!.Target.Bounds.Y) / scale - fontSize - 2;
        return Math.Max(0, topDip);
    }

}

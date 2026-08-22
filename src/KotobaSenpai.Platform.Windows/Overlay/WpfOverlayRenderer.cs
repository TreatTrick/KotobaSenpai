using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// WPF implementation of the <see cref="IOverlayRenderer"/> port: a transparent, topmost, non-activating, click-through
/// tool window that draws one underline for each local merged word. Hovering a local word recolors its underline and shows
/// its LLM meaning popup; hovering a group recolors the underlines of its member words and shows the group detail popup.
/// Clicks always pass through to the window below.
/// </summary>
public sealed class WpfOverlayRenderer : IOverlayRenderer
{
    private readonly IStringLocalizer? _localizer;
    private readonly ISettingsService? _settings;
    private OverlayWindow? _window;

    public WpfOverlayRenderer(IStringLocalizer? localizer = null, ISettingsService? settings = null)
    {
        _localizer = localizer;
        _settings = settings;
    }

    public void Show(WordOverlaySession session)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window ??= new OverlayWindow(_localizer);
            _window.Render(session, ResolveFontScale());
        });
    }

    /// <summary>Reads the configurable furigana font scale (proportion of OCR text height); missing/invalid falls back to the default 1/3.</summary>
    private double ResolveFontScale()
        => FuriganaSettings.ResolveFontScale(_settings?.GetValue(FuriganaSettings.FontScaleKey));

    public void Hide()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window?.StopHover();
            _window?.Hide();
        });
    }

    private sealed class OverlayWindow : Window
    {
        private static readonly Brush DefaultLineBrush = Brushes.DeepSkyBlue;
        private static readonly Brush HoverLineBrush = Brushes.OrangeRed;
        private static readonly Brush PitchHighBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        private static readonly Brush PitchLowBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xFF));
        private static readonly Brush PitchHeibanBrush = new SolidColorBrush(Color.FromRgb(0x88, 0xFF, 0x88));
        private static readonly Effect FuriganaOutline = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 1,
            ShadowDepth = 0,
            Opacity = 1,
        };

        private const int HoverPollMs = 50;

        private readonly Canvas _canvas = new();
        private readonly List<Border> _lineElements = new();
        private readonly List<int> _lineOwner = new(); // parallel to _lineElements: the word index each line belongs to
        private readonly DispatcherTimer _hoverTimer;
        private readonly PhrasePopup _phrasePopup;
        private WordOverlaySession? _session;
        private int _hoverIndex = -1;
        private int _hoveredGroup = -1;
        private IReadOnlyList<int> _hoveredGroupLines = Array.Empty<int>();

        public OverlayWindow(IStringLocalizer? localizer = null)
        {
            _phrasePopup = new PhrasePopup(localizer);

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            IsHitTestVisible = false;
            Content = _canvas;
            SourceInitialized += (_, _) => SetClickThrough();

            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverPollMs) };
            _hoverTimer.Tick += (_, _) => PollHover();
        }

        public void Render(WordOverlaySession session, double fontScale)
        {
            if (!IsVisible)
                Show();

            _session = session;
            _hoverIndex = -1;
            _hoveredGroup = -1;
            _hoveredGroupLines = Array.Empty<int>();

            var handle = new WindowInteropHelper(this).Handle;
            var scale = NativeMethods.GetDpiForWindow(handle) / 96d;
            Width = session.Target.Bounds.Width / scale;
            Height = session.Target.Bounds.Height / scale;
            NativeMethods.SetWindowPos(
                handle,
                nint.Zero,
                session.Target.Bounds.X,
                session.Target.Bounds.Y,
                0,
                0,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
            _canvas.Children.Clear();
            _lineElements.Clear();
            _lineOwner.Clear();

            // One underline per word rect (a cross-line word has one per line); kanji words get hiragana
            // furigana centered above the word's union bounds. Furigana is cleared with the underlines on
            // refresh, and the click-through window never receives mouse events, so hover logic stays untouched.
            for (int wordIndex = 0; wordIndex < session.Words.Count; wordIndex++)
            {
                var word = session.Words[wordIndex];
                if (session.ShouldUnderline(word))
                {
                    foreach (var rect in word.Rects)
                    {
                        var element = MakeBox(rect.Width / scale, 2.0 / scale, DefaultLineBrush);
                        Canvas.SetLeft(element, (rect.X - session.Target.Bounds.X) / scale);
                        Canvas.SetTop(element, (Math.Max(rect.Y, rect.Bottom - 2) - session.Target.Bounds.Y) / scale);
                        _canvas.Children.Add(element);
                        _lineElements.Add(element);
                        _lineOwner.Add(wordIndex);
                    }
                }

                if (!string.IsNullOrEmpty(word.Reading))
                    AddPitchAnnotations(word, scale, fontScale);
            }
            _hoverTimer.Start();
        }

        /// <summary>
        /// Adds the hiragana reading for just the word's kanji portion, centered above the leading kanji run
        /// (okurigana is left as normal text, not annotated), clamped to the overlay top. It positions by character
        /// ratio because Japanese full-width glyphs (kanji and kana) are ~1em wide, so the leading kanji occupy
        /// (kanjiCharCount / surface.Length) of the word's width. // ponytail: multi-em-width edge cases (e.g. 全角・記号) are ignored.
        /// </summary>
        private void AddPitchAnnotations(GroupedWord word, double scale, double fontScale)
        {
            if (word.PitchAccents.Count == 0)
            {
                AddMonochromeFurigana(word.Surface, word.Reading, 0, word, scale, fontScale);
                return;
            }

            foreach (var segment in word.PitchAccents)
            {
                var pattern = PitchAccent.CreatePattern(Kana.ToHiragana(segment.Reading), segment.AccentPosition);
                if (FuriganaSettings.ContainsKanji(segment.Surface) && !string.IsNullOrEmpty(segment.Reading))
                {
                    if (pattern is null)
                        AddMonochromeFurigana(segment.Surface, segment.Reading, segment.SurfaceOffset, word, scale, fontScale);
                    else
                        AddColoredFurigana(segment, pattern, word, scale, fontScale);
                }

                if (pattern is not null)
                    AddPitchDots(segment, pattern, word, scale, fontScale);
            }
        }

        private void AddMonochromeFurigana(
            string surface,
            string reading,
            int surfaceOffset,
            GroupedWord word,
            double scale,
            double fontScale)
        {
            var (kanjiChars, kanjiReading) = FuriganaSettings.OkuriganaTrim(surface, reading);
            if (kanjiChars == 0 || string.IsNullOrEmpty(kanjiReading))
                return;

            var bounds = SegmentBounds(word, surfaceOffset, surface.Length);
            var fontSize = (bounds.Height / scale) * fontScale;
            var text = new TextBlock
            {
                Text = Kana.ToHiragana(kanjiReading),
                FontSize = fontSize,
                Foreground = DefaultLineBrush,
                Effect = FuriganaOutline,
            };
            AddTextCentered(text, bounds, kanjiChars, surface.Length, scale, fontSize);
        }

        private void AddColoredFurigana(
            PitchAccentSummary segment,
            PitchAccentPattern pattern,
            GroupedWord word,
            double scale,
            double fontScale)
        {
            var (kanjiChars, kanjiReading) = FuriganaSettings.OkuriganaTrim(segment.Surface, segment.Reading);
            var kanjiMorae = PitchAccent.SplitMorae(Kana.ToHiragana(kanjiReading));
            if (kanjiChars == 0 || kanjiMorae.Count == 0)
                return;

            var bounds = SegmentBounds(word, segment.SurfaceOffset, segment.Surface.Length);
            var fontSize = (bounds.Height / scale) * fontScale;
            var fontMorae = pattern.Morae.Take(kanjiMorae.Count).ToArray();
            var highMorae = pattern.HighMorae.Take(fontMorae.Length).ToArray();
            var textBlocks = new List<TextBlock>(fontMorae.Length);
            var totalWidth = 0d;
            foreach (var mora in fontMorae)
            {
                var block = new TextBlock { Text = mora, FontSize = fontSize, Effect = FuriganaOutline };
                block.Measure(new Size(double.PositiveInfinity, fontSize));
                textBlocks.Add(block);
                totalWidth += block.DesiredSize.Width;
            }

            var startX = (bounds.X - _session!.Target.Bounds.X) / scale
                + (bounds.Width / scale - totalWidth) / 2;
            var top = TopDip(bounds, scale, fontSize);
            var cursor = startX;
            for (var i = 0; i < textBlocks.Count; i++)
            {
                var block = textBlocks[i];
                block.Foreground = highMorae[i] ? PitchHighBrush : PitchLowBrush;
                _canvas.Children.Add(block);
                Canvas.SetLeft(block, Math.Max(0, cursor));
                Canvas.SetTop(block, top);
                cursor += block.DesiredSize.Width;
            }

            if (segment.AccentPosition == 0 && pattern.Morae.Count > 1)
                AddHeibanMarker(cursor, top + fontSize / 2, fontSize);
        }

        private void AddPitchDots(
            PitchAccentSummary segment,
            PitchAccentPattern pattern,
            GroupedWord word,
            double scale,
            double fontScale)
        {
            var kanaRuns = FuriganaSettings.GetPitchMoraRanges(segment.Surface, pattern.Morae.Count);
            if (kanaRuns.Count == 0)
                return;

            var bounds = SegmentBounds(word, segment.SurfaceOffset, segment.Surface.Length);
            var fontSize = (bounds.Height / scale) * fontScale;
            var dotRadius = Math.Max(2d, fontSize / 4d);
            var centerY = (bounds.Y - _session!.Target.Bounds.Y) / scale - dotRadius - 2;
            if (centerY < dotRadius)
                centerY = dotRadius;

            foreach (var (start, length, moraStart, count) in kanaRuns)
            {
                var left = bounds.X + bounds.Width * start / segment.Surface.Length;
                var width = bounds.Width * length / segment.Surface.Length;
                for (var i = 0; i < count; i++)
                {
                    var centerX = (left - _session.Target.Bounds.X) / scale
                        + width / scale * (i + 0.5) / count;
                    var dot = new Border
                    {
                        Width = dotRadius * 2,
                        Height = dotRadius * 2,
                        CornerRadius = new CornerRadius(dotRadius),
                        Background = pattern.HighMorae[moraStart + i] ? PitchHighBrush : PitchLowBrush,
                    };
                    _canvas.Children.Add(dot);
                    Canvas.SetLeft(dot, centerX - dotRadius);
                    Canvas.SetTop(dot, centerY - dotRadius);
                }

                if (segment.AccentPosition == 0 && moraStart + count == pattern.Morae.Count && pattern.Morae.Count > 1)
                    AddHeibanMarker((left - _session.Target.Bounds.X) / scale + width / scale, centerY, fontSize);
            }
        }

        private void AddTextCentered(TextBlock text, ScreenRect bounds, int annotatedChars, int surfaceLength, double scale, double fontSize)
        {
            _canvas.Children.Add(text);
            text.Measure(new Size(double.PositiveInfinity, fontSize));
            var annotatedWidth = bounds.Width / scale * annotatedChars / surfaceLength;
            var center = (bounds.X - _session!.Target.Bounds.X) / scale + annotatedWidth / 2;
            Canvas.SetLeft(text, Math.Max(0, center - text.DesiredSize.Width / 2));
            Canvas.SetTop(text, TopDip(bounds, scale, fontSize));
        }

        private void AddHeibanMarker(double x, double y, double fontSize)
        {
            var marker = new Border
            {
                Width = Math.Max(3, fontSize / 3),
                Height = Math.Max(1, fontSize / 8),
                Background = PitchHeibanBrush,
            };
            _canvas.Children.Add(marker);
            Canvas.SetLeft(marker, x + 1);
            Canvas.SetTop(marker, y);
        }

        private ScreenRect SegmentBounds(GroupedWord word, int surfaceOffset, int surfaceLength)
        {
            var start = word.Bounds.X + word.Bounds.Width * surfaceOffset / Math.Max(1, word.Surface.Length);
            var width = word.Bounds.Width * surfaceLength / Math.Max(1, word.Surface.Length);
            return new ScreenRect(start, word.Bounds.Y, Math.Max(1, width), word.Bounds.Height);
        }

        private double TopDip(ScreenRect bounds, double scale, double fontSize)
        {
            var topDip = (bounds.Y - _session!.Target.Bounds.Y) / scale - fontSize - 2;
            return Math.Max(0, topDip);
        }

        public void StopHover()
        {
            _hoverTimer.Stop();
            _hoverIndex = -1;
            _hoveredGroup = -1;
            _hoveredGroupLines = Array.Empty<int>();
            _phrasePopup.HidePopup();
            foreach (var element in _lineElements)
                element.Background = DefaultLineBrush;
        }

        /// <summary>
        /// Polls the cursor position for hit testing (the window is click-through and receives no mouse events of its own).
        /// Tests phrase groups first (overlaps resolved by fewer tokens, then provider order), highlighting the underlines of
        /// the group's member words; otherwise falls back to the local-word hot zone.
        /// </summary>
        private void PollHover()
        {
            var session = _session;
            if (session is null || (session.Words.Count == 0 && session.PhraseGroups.Count == 0))
                return;

            if (!NativeMethods.GetCursorPos(out var cursor))
                return;

            var groupHit = PhraseHoverResolver.Resolve(session.PhraseGroups, cursor.X, cursor.Y);
            if (groupHit != _hoveredGroup)
            {
                foreach (var i in _hoveredGroupLines)
                    if (i >= 0 && i < _lineElements.Count)
                        _lineElements[i].Background = DefaultLineBrush;
                _hoveredGroupLines = Array.Empty<int>();
                _hoveredGroup = groupHit;
                if (groupHit >= 0)
                {
                    _hoveredGroupLines = session.GetCoveredWordIndices(session.PhraseGroups[groupHit]);
                    foreach (var i in _hoveredGroupLines)
                        if (i >= 0 && i < _lineElements.Count)
                            _lineElements[i].Background = HoverLineBrush;
                    ShowPhrasePopup(session.PhraseGroups[groupHit]);
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
                return;
            }

            // Local-word hot zone: hit any rect of a word; on overlap, the one whose union center is nearest.
            int hit = -1;
            double best = double.MaxValue;
            for (int i = 0; i < session.Words.Count; i++)
            {
                if (!session.Words[i].Rects.Any(rect => Contains(rect, cursor)))
                    continue;
                var b = session.Words[i].Bounds;
                var cx = b.X + b.Width / 2d;
                var cy = b.Y + b.Height / 2d;
                var d = (cursor.X - cx) * (cursor.X - cx) + (cursor.Y - cy) * (cursor.Y - cy);
                if (d < best)
                {
                    best = d;
                    hit = i;
                }
            }

            if (hit == _hoverIndex)
                return;
            UnhighlightWord(_hoverIndex);
            _hoverIndex = hit;
            if (hit >= 0)
                foreach (var li in WordLineIndices(hit))
                    if (li < _lineElements.Count)
                        _lineElements[li].Background = HoverLineBrush;
            UpdatePopup(hit);
        }

        private void UnhighlightWord(int wordIndex)
        {
            if (wordIndex < 0)
                return;
            foreach (var li in WordLineIndices(wordIndex))
                if (li < _lineElements.Count)
                    _lineElements[li].Background = DefaultLineBrush;
        }

        private IEnumerable<int> WordLineIndices(int wordIndex)
            => Enumerable.Range(0, _lineOwner.Count).Where(li => _lineOwner[li] == wordIndex);

        private static bool Contains(ScreenRect rect, NativeMethods.POINT p)
            => p.X >= rect.X && p.X <= rect.Right && p.Y >= rect.Y && p.Y <= rect.Bottom;

        private void ShowPhrasePopup(PhraseGroupView group)
        {
            var anchor = group.Parts.SelectMany(part => part.Rects).FirstOrDefault();
            if (anchor == default)
            {
                _phrasePopup.HidePopup();
                return;
            }
            var session = _session;
            var memberMeanings = session is null
                ? Array.Empty<WordMeaningView>()
                : session.GetCoveredWordMeanings(group);
            _phrasePopup.ShowResult(group.Label, group.Meaning, group.Grammar, anchor, memberMeanings);
        }

        /// <summary>Shows the hovered word's LLM meaning popup, or headword + reading + "no meaning" when the provider returned none.</summary>
        private void UpdatePopup(int hit)
        {
            var session = _session;
            if (session is null || hit < 0 || hit >= session.Words.Count)
            {
                _phrasePopup.HidePopup();
                return;
            }

            var word = session.Words[hit];
            var meaning = session.TryGetMeaning(word);
            if (meaning is not null)
                _phrasePopup.ShowWordMeaning(meaning, word.Bounds);
            else
                _phrasePopup.ShowWordWithoutMeaning(WordMeaningView.FromWord(word), word.Bounds);
        }

        private static Border MakeBox(double width, double height, Brush background)
            => new()
            {
                Width = width,
                Height = height,
                Background = background,
                CornerRadius = new CornerRadius(height / 2),
            };

        private void SetClickThrough()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle,
                new nint(style | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
        }
    }

    private static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const long WsExTransparent = 0x20;
        public const long WsExNoActivate = 0x08000000;
        public const long WsExToolWindow = 0x80;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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
                foreach (var rect in session.Words[wordIndex].Rects)
                {
                    var element = MakeBox(rect.Width / scale, 2.0 / scale, DefaultLineBrush);
                    Canvas.SetLeft(element, (rect.X - session.Target.Bounds.X) / scale);
                    Canvas.SetTop(element, (Math.Max(rect.Y, rect.Bottom - 2) - session.Target.Bounds.Y) / scale);
                    _canvas.Children.Add(element);
                    _lineElements.Add(element);
                    _lineOwner.Add(wordIndex);
                }

                var word = session.Words[wordIndex];
                if (FuriganaSettings.ContainsKanji(word.Surface) && !string.IsNullOrEmpty(word.Reading))
                    AddFurigana(word, scale, fontScale);
            }
            _hoverTimer.Start();
        }

        /// <summary>
        /// Adds the hiragana reading for just the word's kanji portion, centered above the leading kanji run
        /// (okurigana is left as normal text, not annotated), clamped to the overlay top. It positions by character
        /// ratio because Japanese full-width glyphs (kanji and kana) are ~1em wide, so the leading kanji occupy
        /// (kanjiCharCount / surface.Length) of the word's width. // ponytail: multi-em-width edge cases (e.g. 全角・記号) are ignored.
        /// </summary>
        private void AddFurigana(GroupedWord word, double scale, double fontScale)
        {
            var (kanjiChars, kanjiReading) = FuriganaSettings.OkuriganaTrim(word.Surface, word.Reading);
            if (kanjiChars == 0 || string.IsNullOrEmpty(kanjiReading))
                return;

            var bounds = word.Bounds;
            var fontSize = (bounds.Height / scale) * fontScale;
            var gap = 2.0;
            var topDip = (bounds.Y - _session!.Target.Bounds.Y) / scale - fontSize - gap;
            if (topDip < 0)
                topDip = 0; // clamp to overlay top so it never draws off-window

            var text = new TextBlock
            {
                Text = Kana.ToHiragana(kanjiReading),
                FontSize = fontSize,
                Foreground = DefaultLineBrush, // 天蓝色，与下划线一致
            };
            _canvas.Children.Add(text);
            text.Measure(new Size(double.PositiveInfinity, fontSize));
            var kanjiWidthDip = (bounds.Width / scale) * ((double)kanjiChars / word.Surface.Length);
            var centerDip = (bounds.X - _session.Target.Bounds.X) / scale + kanjiWidthDip / 2;
            Canvas.SetLeft(text, Math.Max(0, centerDip - text.DesiredSize.Width / 2));
            Canvas.SetTop(text, topDip);
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
                : session.GetCoveredWordIndices(group)
                    .Select(i => session.Words[i])
                    .Select(session.TryGetMeaning)
                    .Where(meaning => meaning is not null)
                    .Cast<WordMeaningView>()
                    .ToArray();
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
                _phrasePopup.ShowWordWithoutMeaning(word.Surface, word.Reading, word.Bounds);
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

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
    private OverlayWindow? _window;

    public WpfOverlayRenderer(IStringLocalizer? localizer = null)
    {
        _localizer = localizer;
    }

    public void Show(WordOverlaySession session)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window ??= new OverlayWindow(_localizer);
            _window.Render(session);
        });
    }

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

        public void Render(WordOverlaySession session)
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

            foreach (var line in session.Lines)
            {
                var element = MakeBox(line.Width / scale, line.Thickness / scale, DefaultLineBrush);
                Canvas.SetLeft(element, (line.X - session.Target.Bounds.X) / scale);
                Canvas.SetTop(element, (line.Y - session.Target.Bounds.Y) / scale);
                _canvas.Children.Add(element);
                _lineElements.Add(element);
            }
            _hoverTimer.Start();
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
                if (_hoverIndex >= 0 && _hoverIndex < _lineElements.Count)
                    _lineElements[_hoverIndex].Background = DefaultLineBrush;
                _hoverIndex = -1;
                return;
            }

            // Local-word hot zone: pick the word containing the cursor; on overlap, the one whose center is nearest.
            int hit = -1;
            double best = double.MaxValue;
            for (int i = 0; i < session.Words.Count; i++)
            {
                var b = session.Words[i].Bounds;
                if (cursor.X < b.X || cursor.X > b.Right || cursor.Y < b.Y || cursor.Y > b.Bottom)
                    continue;
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
            if (_hoverIndex >= 0 && _hoverIndex < _lineElements.Count)
                _lineElements[_hoverIndex].Background = DefaultLineBrush;
            _hoverIndex = hit;
            if (hit >= 0 && hit < _lineElements.Count)
                _lineElements[hit].Background = HoverLineBrush;
            UpdatePopup(hit);
        }

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
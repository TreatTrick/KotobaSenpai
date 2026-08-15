using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;
using KotobaSenpai.Core.Services;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// 端口 <see cref="IOverlayRenderer"/> 的 WPF 实现：
/// 透明、置顶、不激活、点击穿透的工具窗口，为每个分词词绘制一条下划线，并为每个 phrase group 的
/// 每个 part 绘制组合词块标记。悬停本地词时该词整条线变色；悬停 group 任一 part 时高亮其全部 parts
/// 并打开一个详情弹窗。点击始终穿透到下方窗口。
/// </summary>
public sealed class WpfOverlayRenderer : IOverlayRenderer
{
    private readonly IDictionaryLookup _lookup;
    private OverlayWindow? _window;

    public WpfOverlayRenderer(IDictionaryLookup lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public void Show(WordOverlaySession session)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window ??= new OverlayWindow(_lookup);
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
        private static readonly Color DefaultPartColor = Color.FromRgb(0x7A, 0xC0, 0xFF);

        private const int HoverPollMs = 50;

        private readonly IDictionaryLookup _lookup;
        private readonly Canvas _canvas = new();
        private readonly List<Border> _lineElements = new();
        private readonly List<(Border Element, int GroupIndex)> _partElements = new();
        private readonly DispatcherTimer _hoverTimer;
        private readonly DictionaryPopup _popup;
        private readonly PhrasePopup? _phrasePopup;
        private readonly Dictionary<string, IReadOnlyList<DictionaryEntry>> _lookupCache = new();
        private WordOverlaySession? _session;
        private int _hoverIndex = -1;
        private int _hoveredGroup = -1;

        public OverlayWindow(IDictionaryLookup lookup)
        {
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
            _popup = new DictionaryPopup();
            _phrasePopup = new PhrasePopup();

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
            _lookupCache.Clear();

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
            _partElements.Clear();

            foreach (var line in session.Lines)
            {
                var element = MakeBox(line.Width / scale, line.Thickness / scale, DefaultLineBrush);
                Canvas.SetLeft(element, (line.X - session.Target.Bounds.X) / scale);
                Canvas.SetTop(element, (line.Y - session.Target.Bounds.Y) / scale);
                _canvas.Children.Add(element);
                _lineElements.Add(element);
            }

            foreach (var (group, groupIndex) in session.PhraseGroups.Select((g, i) => (g, i)))
            {
                foreach (var part in group.Parts)
                {
                    foreach (var rect in part.Rects)
                    {
                        var element = MakeBox(
                            rect.Width / scale,
                            3.0 / scale,
                            new SolidColorBrush(DefaultPartColor));
                        element.BorderBrush = new SolidColorBrush(DefaultPartColor);
                        element.Opacity = 0.9;
                        Canvas.SetLeft(element, (rect.X - session.Target.Bounds.X) / scale);
                        Canvas.SetTop(element, (rect.Y - session.Target.Bounds.Y) / scale);
                        _canvas.Children.Add(element);
                        _partElements.Add((element, groupIndex));
                    }
                }
            }
            _hoverTimer.Start();
        }

        public void StopHover()
        {
            _hoverTimer.Stop();
            _hoverIndex = -1;
            _hoveredGroup = -1;
            _popup.HidePopup();
            _phrasePopup?.HidePopup();
            foreach (var element in _lineElements)
                element.Background = DefaultLineBrush;
            foreach (var (element, _) in _partElements)
                element.Background = new SolidColorBrush(DefaultPartColor);
        }

        /// <summary>
        /// 轮询光标位置做命中测试（窗口点击穿透、收不到自身鼠标事件）。先测 phrase group
        /// （重叠按更少 token 数、再按 provider 顺序决胜），高亮该 group 全部 parts；否则回退到本地词热区。
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
                if (_hoveredGroup >= 0)
                {
                    foreach (var (element, index) in _partElements.Where(p => p.GroupIndex == _hoveredGroup))
                        element.Background = new SolidColorBrush(DefaultPartColor);
                }
                _hoveredGroup = groupHit;
                if (groupHit >= 0)
                {
                    foreach (var (element, index) in _partElements.Where(p => p.GroupIndex == groupHit))
                        element.Background = HoverLineBrush;
                    ShowPhrasePopup(session.PhraseGroups[groupHit]);
                }
                else
                {
                    _phrasePopup?.HidePopup();
                }
            }

            if (groupHit >= 0)
            {
                // group 命中时挂起本地词悬停，避免两者弹窗打架。
                if (_hoverIndex >= 0 && _hoverIndex < _lineElements.Count)
                    _lineElements[_hoverIndex].Background = DefaultLineBrush;
                _popup.HidePopup();
                _hoverIndex = -1;
                return;
            }

            // 本地词热区：命中取包含光标的词，重叠时取中心最近者。
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
            // 取该 group 首个 part 的首个矩形作为弹窗锚点。
            var anchor = group.Parts.SelectMany(part => part.Rects).FirstOrDefault();
            if (anchor == default)
            {
                _phrasePopup?.HidePopup();
                return;
            }
            _phrasePopup?.ShowResult(group.Label, group.Meaning, group.Grammar, anchor);
        }

        /// <summary>
        /// 悬停词变化时更新弹窗。识别阶段已解析的 span 直接使用其结果；只有旧兼容词块
        /// 才在这里按 token 查词，避免对“已解析但未命中”的词重复访问 SQLite。
        /// </summary>
        private void UpdatePopup(int hit)
        {
            var session = _session;
            if (session is null || hit < 0 || hit >= session.Words.Count)
            {
                _popup.HidePopup();
                return;
            }

            var word = session.Words[hit];
            IReadOnlyList<DictionaryEntry> entries;
            if (word.HasResolvedLookup)
            {
                entries = word.Entries;
            }
            else
            {
                if (!_lookupCache.TryGetValue(word.Token.Lemma, out var cachedEntries))
                {
                    entries = _lookup.Lookup(word.Token);
                    _lookupCache[word.Token.Lemma] = entries;
                }
                else
                {
                    entries = cachedEntries;
                }
            }
            _popup.ShowResult(entries, word.Reading, word.Bounds);
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
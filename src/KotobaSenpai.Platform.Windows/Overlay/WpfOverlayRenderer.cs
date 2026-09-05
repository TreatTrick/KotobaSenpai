using KotobaSenpai.Core.Overlay;
using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.WordGrouping;
using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Geometry;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// WPF implementation of the <see cref="IOverlayRenderer"/> port: a transparent, target-relative, non-activating, click-through
/// tool window that draws one underline for each local merged word. Hovering a local word recolors its underline and shows
/// its LLM meaning popup; hovering a group recolors the underlines of its member words and shows the group detail popup.
/// Clicks always pass through to the window below.
/// </summary>
public sealed class WpfOverlayRenderer : IOverlayRenderer
{
    private readonly IStringLocalizer? _localizer;
    private readonly ISettingsService? _settings;
    private readonly ITargetWindowTracker? _tracker;
    private readonly ILogger? _logger;
    private OverlayWindow? _window;

    public WpfOverlayRenderer(
        IStringLocalizer? localizer = null,
        ISettingsService? settings = null,
        ITargetWindowTracker? tracker = null,
        ILogger? logger = null)
    {
        _localizer = localizer;
        _settings = settings;
        _tracker = tracker;
        _logger = logger;
    }

    public void Show(WordOverlaySession session)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Show requested: sessionId={0}, targetHandle={1}, words={2}, underlines={3}, phraseGroups={4}, trackerAttached={5}.",
                session.Id,
                session.Target.Handle,
                session.Words.Count,
                session.Lines.Count,
                session.PhraseGroups.Count,
                _tracker is not null);
            _window ??= new OverlayWindow(_localizer, _tracker, _logger);
            _window.Render(session, ResolveFontScale());
        });
    }

    /// <summary>Reads the configurable furigana font scale (proportion of OCR text height); missing/invalid falls back to the default 1/3.</summary>
    private double ResolveFontScale()
        => FuriganaSettings.ResolveFontScale(_settings?.GetValue(FuriganaSettings.FontScaleKey));

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

    public void Hide()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _logger?.Log(LogLevel.Debug, null, "[DEBUG-OVERLAY] Hide requested.");
            _window?.ClearSession();
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
        private readonly ITargetWindowTracker? _tracker;
        private readonly ILogger? _logger;
        private WordOverlaySession? _session;
        private nint _ownerHandle;
        private double _fontScale;
        private int _hoverIndex = -1;
        private int _hoveredGroup = -1;
        private IReadOnlyList<int> _hoveredGroupLines = Array.Empty<int>();

        public OverlayWindow(
            IStringLocalizer? localizer = null,
            ITargetWindowTracker? tracker = null,
            ILogger? logger = null)
        {
            _phrasePopup = new PhrasePopup(localizer);
            _tracker = tracker;
            _logger = logger;

            if (_tracker is not null)
                _tracker.Changed += OnTargetChanged;
            Closed += (_, _) =>
            {
                if (_tracker is not null)
                    _tracker.Changed -= OnTargetChanged;
                OverlayWindowRegistry.Unregister(new WindowInteropHelper(this).Handle);
            };

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = false;
            ShowActivated = false;
            IsHitTestVisible = false;
            Content = _canvas;
            SourceInitialized += (_, _) =>
            {
                SetOwnerHandle();
                SetClickThrough();
                OverlayWindowRegistry.Register(new WindowInteropHelper(this).Handle);
            };

            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverPollMs) };
            _hoverTimer.Tick += (_, _) => PollHover();

            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Overlay window configured: topmost={0}, showActivated={1}, hitTestVisible={2}, trackerAttached={3}.",
                Topmost,
                ShowActivated,
                IsHitTestVisible,
                _tracker is not null);
        }

        public void Render(WordOverlaySession session, double fontScale)
        {
            _fontScale = fontScale;
            _session = session;
            _ownerHandle = session.Target.Handle;
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Render requested: sessionId={0}, targetHandle={1}, visible={2}, topmost={3}, words={4}, underlines={5}, phraseGroups={6}.",
                session.Id,
                session.Target.Handle,
                IsVisible,
                Topmost,
                session.Words.Count,
                session.Lines.Count,
                session.PhraseGroups.Count);
            if (_tracker is not null)
            {
                var snapshot = _tracker.Current;
                if (snapshot is null || snapshot.Handle != session.Target.Handle)
                {
                    _logger?.Log(
                        LogLevel.Debug,
                        null,
                        "[DEBUG-OVERLAY] Render skipped: tracker snapshot missing or handle mismatch. sessionId={0}, sessionTargetHandle={1}, snapshotHandle={2}.",
                        session.Id,
                        session.Target.Handle,
                        snapshot?.Handle.ToString() ?? "<none>");
                    ClearSession();
                    Hide();
                    return;
                }
                if (!snapshot.IsValid)
                {
                    _logger?.Log(
                        LogLevel.Debug,
                        null,
                        "[DEBUG-OVERLAY] Render skipped and session cleared: targetHandle={0}, target is invalid.",
                        snapshot.Handle);
                    ClearSession();
                    Hide();
                    return;
                }
                if (!snapshot.IsRenderable)
                {
                    _logger?.Log(
                        LogLevel.Debug,
                        null,
                        "[DEBUG-OVERLAY] Render skipped and window hidden: targetHandle={0}, visible={1}, minimized={2}, foreground={3}, occluded={4}, capturable={5}, renderable={6}, overlayVisible={7}, topmost={8}.",
                        snapshot.Handle,
                        snapshot.IsVisible,
                        snapshot.IsMinimized,
                        snapshot.IsForeground,
                        snapshot.IsOccluded,
                        snapshot.IsCapturable,
                        snapshot.IsRenderable,
                        IsVisible,
                        Topmost);
                    StopHover();
                    Hide();
                    return;
                }
                session = session.Reproject(snapshot.Target);
            }

            RenderCurrent(session, fontScale);
        }

        private void OnTargetChanged(object? sender, TargetWindowSnapshot snapshot)
        {
            if (_session is null)
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[DEBUG-OVERLAY] Tracker change ignored: no active overlay session. handle={0}, visible={1}, minimized={2}, foreground={3}, occluded={4}, renderable={5}.",
                    snapshot.Handle,
                    snapshot.IsVisible,
                    snapshot.IsMinimized,
                    snapshot.IsForeground,
                    snapshot.IsOccluded,
                    snapshot.IsRenderable);
                return;
            }

            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Tracker change received: sessionId={0}, handle={1}, visible={2}, minimized={3}, foreground={4}, occluded={5}, capturable={6}, renderable={7}, overlayVisible={8}, topmost={9}.",
                _session.Id,
                snapshot.Handle,
                snapshot.IsVisible,
                snapshot.IsMinimized,
                snapshot.IsForeground,
                snapshot.IsOccluded,
                snapshot.IsCapturable,
                snapshot.IsRenderable,
                IsVisible,
                Topmost);
            if (snapshot.Handle != _session.Target.Handle)
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[DEBUG-OVERLAY] Tracker change caused overlay hide: sessionTargetHandle={0}, snapshotHandle={1}.",
                    _session.Target.Handle,
                    snapshot.Handle);
                ClearSession();
                Hide();
                return;
            }
            if (!snapshot.IsValid)
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[DEBUG-OVERLAY] Tracker change cleared overlay session because target is invalid: handle={0}.",
                    snapshot.Handle);
                ClearSession();
                Hide();
                return;
            }
            if (!snapshot.IsRenderable)
            {
                _logger?.Log(
                    LogLevel.Debug,
                    null,
                    "[DEBUG-OVERLAY] Tracker change caused overlay hide because target is not renderable: handle={0}, foreground={1}, occluded={2}.",
                    snapshot.Handle,
                    snapshot.IsForeground,
                    snapshot.IsOccluded);
                StopHover();
                Hide();
                return;
            }

            _ownerHandle = snapshot.Handle;
            RenderCurrent(_session.Reproject(snapshot.Target), _fontScale);
        }

        private void RenderCurrent(WordOverlaySession session, double fontScale)
        {
            _ownerHandle = session.Target.Handle;
            SetOwnerHandle();
            StopHover();
            var wasVisible = IsVisible;
            if (!IsVisible)
                Show();

            _session = session;
            _hoverIndex = -1;
            _hoveredGroup = -1;
            _hoveredGroupLines = Array.Empty<int>();

            var handle = new WindowInteropHelper(this).Handle;
            var dpi = NativeMethods.GetDpiForWindow(handle);
            var scale = (dpi == 0 ? 96 : dpi) / 96d;
            Width = session.Target.Bounds.Width / scale;
            Height = session.Target.Bounds.Height / scale;
            var insertAfter = NativeMethods.GetWindow(session.Target.Handle, NativeMethods.GwHwndPrev);
            var flags = NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow;
            if (insertAfter == handle)
            {
                insertAfter = nint.Zero;
                flags |= NativeMethods.SwpNoZOrder;
            }
            NativeMethods.SetWindowPos(
                handle,
                insertAfter,
                session.Target.Bounds.X,
                session.Target.Bounds.Y,
                0,
                0,
                flags | NativeMethods.SwpNoSize);
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

            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Overlay rendered: hwnd={0}, wasVisible={1}, visible={2}, topmost={3}, targetHandle={4}, targetBounds={5}, words={6}, underlines={7}, phraseGroups={8}, canvasChildren={9}.",
                handle,
                wasVisible,
                IsVisible,
                Topmost,
                session.Target.Handle,
                session.Target.Bounds,
                session.Words.Count,
                _lineElements.Count,
                session.PhraseGroups.Count,
                _canvas.Children.Count);
        }

        public void ClearSession()
        {
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Session cleared: sessionId={0}, overlayVisible={1}, topmost={2}, words={3}, underlines={4}, phraseGroups={5}.",
                _session?.Id.ToString() ?? "<none>",
                IsVisible,
                Topmost,
                _session?.Words.Count ?? 0,
                _lineElements.Count,
                _session?.PhraseGroups.Count ?? 0);
            _session = null;
            StopHover();
            _canvas.Children.Clear();
            _lineElements.Clear();
            _lineOwner.Clear();
        }

        private void SetOwnerHandle()
        {
            if (_ownerHandle == nint.Zero)
                return;
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != nint.Zero)
                helper.Owner = _ownerHandle;
        }

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

                var bounds = SegmentBounds(word, sourceTokenIndex, segment.SurfaceStart, segment.SurfaceLength);
                var fontSize = (bounds.Height / scale) * fontScale;
                var text = new TextBlock
                {
                    Text = Kana.ToHiragana(segment.Reading),
                    FontSize = fontSize,
                    Foreground = DefaultLineBrush,
                    Effect = FuriganaOutline,
                };
                AddTextCentered(text, bounds, segment.SurfaceLength, segment.SurfaceLength, scale, fontSize);
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

            var bounds = SegmentBounds(
                word,
                sourceTokenIndex,
                furiganaSegment.SurfaceStart,
                furiganaSegment.SurfaceLength);
            var fontSize = (bounds.Height / scale) * fontScale;
            var fontMorae = pattern.Morae.Skip(moraOffset).Take(moraCount).ToArray();
            var highMorae = pattern.HighMorae.Skip(moraOffset).Take(fontMorae.Length).ToArray();
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

            if (addHeibanMarker && pattern.AccentPosition == 0 && pattern.Morae.Count > 1)
                AddHeibanMarker(cursor, top + fontSize / 2, fontSize);
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
            var kanaRuns = FuriganaSettings.GetPitchMoraRanges(kanaSurface, kanaSurface, moraCount);
            if (kanaRuns.Count == 0)
                return;

            var bounds = SegmentBounds(
                word,
                sourceTokenIndex,
                furiganaSegment.SurfaceStart,
                furiganaSegment.SurfaceLength);
            var fontSize = (bounds.Height / scale) * fontScale;
            var dotRadius = Math.Max(2d, fontSize / 4d);
            var centerY = (bounds.Y - _session!.Target.Bounds.Y) / scale - dotRadius - 2;
            if (centerY < dotRadius)
                centerY = dotRadius;

            foreach (var (start, length, moraStart, count) in kanaRuns)
            {
                var left = bounds.X + bounds.Width * start / furiganaSegment.SurfaceLength;
                var width = bounds.Width * length / furiganaSegment.SurfaceLength;
                for (var i = 0; i < count; i++)
                {
                    var centerX = (left - _session.Target.Bounds.X) / scale
                        + width / scale * (i + 0.5) / count;
                    var dot = new Border
                    {
                        Width = dotRadius * 2,
                        Height = dotRadius * 2,
                        CornerRadius = new CornerRadius(dotRadius),
                        Background = pattern.HighMorae[moraOffset + moraStart + i] ? PitchHighBrush : PitchLowBrush,
                    };
                    _canvas.Children.Add(dot);
                    Canvas.SetLeft(dot, centerX - dotRadius);
                    Canvas.SetTop(dot, centerY - dotRadius);
                }

                if (segment.AccentPosition == 0
                    && moraOffset + moraStart + count == pattern.Morae.Count
                    && pattern.Morae.Count > 1)
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

        private ScreenRect SegmentBounds(GroupedWord word, int sourceTokenIndex, int surfaceOffset, int surfaceLength)
        {
            if (sourceTokenIndex < 0 || sourceTokenIndex >= word.SourceTokenLines.Count)
                return word.Bounds;

            var end = surfaceOffset + surfaceLength;
            var matching = word.SourceTokenLines[sourceTokenIndex]
                .Where(line => line.SurfaceOffset < end
                    && line.SurfaceOffset + line.SurfaceLength > surfaceOffset)
                .Select(line =>
                {
                    var start = Math.Max(surfaceOffset, line.SurfaceOffset) - line.SurfaceOffset;
                    var finish = Math.Min(end, line.SurfaceOffset + line.SurfaceLength) - line.SurfaceOffset;
                    var left = line.Bounds.X + line.Bounds.Width * start / line.SurfaceLength;
                    var right = line.Bounds.X + line.Bounds.Width * finish / line.SurfaceLength;
                    return new ScreenRect(left, line.Bounds.Y, Math.Max(1, right - left), line.Bounds.Height);
                })
                .ToArray();
            if (matching.Length == 0)
                return word.Bounds;
            return Union(matching);
        }

        private static ScreenRect Union(IReadOnlyList<ScreenRect> rects)
        {
            int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
            foreach (var rect in rects)
            {
                x1 = Math.Min(x1, rect.X);
                y1 = Math.Min(y1, rect.Y);
                x2 = Math.Max(x2, rect.Right);
                y2 = Math.Max(y2, rect.Bottom);
            }
            return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
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
                    _hoveredGroupLines = GetLineIndicesForWords(
                        session.GetCoveredWordIndices(session.PhraseGroups[groupHit]),
                        _lineOwner);
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
                _phrasePopup.ShowWordWithoutMeaning(session.GetMeaningOrDefault(word), word.Bounds);
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
            var actualStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            _logger?.Log(
                LogLevel.Debug,
                null,
                "[DEBUG-OVERLAY] Native overlay window configured: hwnd={0}, visible={1}, topmost={2}, exStyle=0x{3:X}.",
                handle,
                IsVisible,
                Topmost,
                actualStyle);
        }
    }

        private static class NativeMethods
        {
        public const int GwlExStyle = -20;
        public const long WsExTransparent = 0x20;
        public const long WsExNoActivate = 0x08000000;
        public const long WsExToolWindow = 0x80;
        public const uint GwHwndPrev = 3;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpShowWindow = 0x0040;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern nint GetWindow(nint hWnd, uint command);

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

using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Geometry;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using System.Runtime.InteropServices;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// Detail popup for both phrase groups and LLM word meanings: shows the group's label/meaning/grammar (plus per-word
/// meanings) or a single word's headword/pos/reading/meaning/grammar. Receives mouse input without activation;
/// the overlay controls its lifetime while the pointer approaches or interacts with it.
/// </summary>
public sealed partial class PhrasePopup : Window
{
    private const double ReadingWidth = 440;
    private const double MinimumWidth = 280;

    private readonly IStringLocalizer? _localizer;
    private readonly ILogger? _logger;
    private readonly PhrasePopupHoverTracker _hoverTracker = new();

    public PhrasePopup(IStringLocalizer? localizer = null, ILogger? logger = null)
    {
        InitializeComponent();
        // Resolve dynamic Fluent brushes from the host while keeping standalone use self-contained.
        if (Application.Current?.TryFindResource("TextFillColorPrimaryBrush") is System.Windows.Media.Brush
            && Application.Current.TryFindResource("SolidBackgroundFillColorBaseBrush") is System.Windows.Media.Brush)
        {
            var fallbackPalette = Resources.MergedDictionaries.First(dictionary =>
                dictionary.Source.OriginalString.EndsWith("PhrasePopupPalette.xaml", StringComparison.Ordinal));
            Resources.MergedDictionaries.Remove(fallbackPalette);
        }
        _localizer = localizer;
        _logger = logger;
        SourceInitialized += (_, _) =>
        {
            ConfigureMouseInput();
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
            OverlayWindowRegistry.Register(handle);
            // WPF resets WindowInteropHelper.Handle before raising Closed.
            Closed += (_, _) => OverlayWindowRegistry.Unregister(handle);
        };
    }

    public void ShowResult(
        string label, string meaning, string grammar, ScreenRect anchor,
        IReadOnlyList<WordMeaningView>? memberMeanings = null, string reading = "")
    {
        DataContext = PhrasePopupViewModel.ForPhrase(label, meaning, grammar, memberMeanings, _localizer, reading);
        ShowAndPosition(anchor);
    }

    /// <summary>Shows a single word's LLM meaning: headword + pos + reading + meaning + grammar.</summary>
    public void ShowWordMeaning(WordMeaningView meaning, ScreenRect anchor)
    {
        DataContext = PhrasePopupViewModel.ForWord(meaning, _localizer);
        ShowAndPosition(anchor);
    }

    /// <summary>Shows a word that has no LLM meaning: headword + reading + a "no meaning" hint.</summary>
    public void ShowWordWithoutMeaning(WordMeaningView meaning, ScreenRect anchor)
    {
        DataContext = PhrasePopupViewModel.ForWord(meaning, _localizer, showMeaning: false);
        ShowAndPosition(anchor);
    }

    public void HidePopup()
    {
        _hoverTracker.Reset();
        if (IsVisible)
            Hide();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _hoverTracker.MarkEntered();
    }

    internal bool ShouldRemainOpen(int cursorX, int cursorY, bool isOverSource)
    {
        if (!IsVisible)
            return false;
        var handle = new WindowInteropHelper(this).Handle;
        if (!Native.GetWindowRect(handle, out var bounds))
            return false;
        var dpi = Native.GetDpiForWindow(handle);
        return _hoverTracker.ShouldRemainOpen(bounds.ToScreenRect(), cursorX, cursorY,
            dpi == 0 ? 1 : dpi / 96d, isOverSource);
    }

    private void ShowAndPosition(ScreenRect anchor)
    {
        _hoverTracker.Reset();
        PopupScrollViewer.ScrollToTop();
        new WindowInteropHelper(this).EnsureHandle();
        Position(anchor);
    }

    private void MeasureContent(ScreenRect workArea, double scale)
    {
        var availableWidth = workArea.Width / scale;
        var availableHeight = workArea.Height / scale;
        var padding = PopupScrollViewer.Padding.Left + PopupScrollViewer.Padding.Right
            + ContentPanel.Margin.Left + ContentPanel.Margin.Right
            + PopupFrame.BorderThickness.Left + PopupFrame.BorderThickness.Right;

        // Realize the current templates before choosing a readable column or a wider headword.
        PopupFrame.Measure(new Size(availableWidth, double.PositiveInfinity));
        PopupFrame.UpdateLayout();
        Heading.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var readingWidth = Math.Min(availableWidth, Math.Max(ReadingWidth, Heading.DesiredSize.Width + padding));
        PopupFrame.Measure(new Size(readingWidth, double.PositiveInfinity));
        PopupFrame.UpdateLayout();
        PopupFrame.Measure(new Size(readingWidth, double.PositiveInfinity));
        Width = Math.Min(availableWidth, Math.Max(MinimumWidth, PopupFrame.DesiredSize.Width));
        PopupFrame.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Min(availableHeight, PopupFrame.DesiredSize.Height);
    }

    private void Position(ScreenRect anchor)
    {
        var anchorRect = new Native.Rect { Left = anchor.X, Top = anchor.Y, Right = anchor.Right, Bottom = anchor.Bottom };
        var monitor = Native.MonitorFromRect(in anchorRect, Native.MonitorDefaultToNearest);
        var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info))
        {
            _logger?.LogWarning("[OVERLAY] Popup monitor work area is unavailable: error={0}.", Marshal.GetLastWin32Error());
            HidePopup();
            return;
        }

        var workArea = info.WorkArea.ToScreenRect();
        var handle = new WindowInteropHelper(this).Handle;
        // Native bounds and anchors share physical pixels; WPF Left/Top use DIPs.
        // A move to another monitor may change the popup's DPI and native size once.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var dpi = Native.GetDpiForWindow(handle);
            var scale = dpi == 0 ? 1 : dpi / 96d;
            MeasureContent(workArea, scale);
            if (!IsVisible)
                Show();
            if (!Native.GetWindowRect(handle, out var window))
            {
                _logger?.LogWarning("[OVERLAY] Popup window bounds are unavailable: error={0}.", Marshal.GetLastWin32Error());
                HidePopup();
                return;
            }
            var gap = (int)Math.Ceiling(4 * scale);
            var placement = CalculatePlacement(anchor, workArea, window.Right - window.Left, window.Bottom - window.Top, gap);
            if (!Native.SetWindowPos(handle, nint.Zero, placement.X, placement.Y, placement.Width, placement.Height,
                Native.SwpNoActivate | Native.SwpNoZOrder))
            {
                _logger?.LogWarning("[OVERLAY] Popup positioning failed: error={0}.", Marshal.GetLastWin32Error());
                HidePopup();
                return;
            }
            _logger?.Log(LogLevel.Trace, null, "[OVERLAY] Popup positioned: anchor={0}, workArea={1}, dpi={2}, bounds={3}.",
                anchor, workArea, dpi, placement);
            if (Native.GetDpiForWindow(handle) == dpi)
                break;
        }
    }

    internal static ScreenRect CalculatePlacement(ScreenRect anchor, ScreenRect workArea, int width, int height, int gap)
    {
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        var x = Math.Clamp(anchor.X, workArea.X, workArea.Right - width);
        var y = anchor.Bottom + gap;
        if (y + height > workArea.Bottom)
            y = anchor.Y - height - gap;
        y = Math.Clamp(y, workArea.Y, workArea.Bottom - height);
        return new ScreenRect(x, y, width, height);
    }

    private void ConfigureMouseInput()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64();
        Native.SetWindowLongPtr(handle, Native.GwlExStyle,
            new nint((style & ~Native.WsExTransparent) | Native.WsExNoActivate | Native.WsExToolWindow));
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.WmMouseActivate)
        {
            handled = true;
            return Native.MaNoActivate;
        }
        if (message == Native.WmMouseWheel)
        {
            // Route directly to the scroll viewer even though this window never takes keyboard focus.
            var delta = unchecked((short)(wParam.ToInt64() >> 16));
            PopupScrollViewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = Mouse.MouseWheelEvent,
            });
            handled = true;
        }
        return nint.Zero;
    }

    private static class Native
    {
        public const int GwlExStyle = -20;
        public const int WmMouseActivate = 0x0021;
        public const int WmMouseWheel = 0x020A;
        public const int MaNoActivate = 3;
        public const long WsExTransparent = 0x20;
        public const long WsExNoActivate = 0x08000000;
        public const long WsExToolWindow = 0x80;
        public const uint MonitorDefaultToNearest = 2;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpNoZOrder = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
            public readonly ScreenRect ToScreenRect() => new(Left, Top, Right - Left, Bottom - Top);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int Size;
            public Rect Bounds, WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        public static extern nint MonitorFromRect(in Rect rect, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint handle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint handle, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    }
}

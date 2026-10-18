using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Geometry;
using System.Windows;
using System.Windows.Interop;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using System.Runtime.InteropServices;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// Detail popup for both phrase groups and LLM word meanings: shows the group's label/meaning/grammar (plus per-word
/// meanings) or a single word's headword/pos/reading/meaning/grammar. Click-through and non-activating; updated by
/// the overlay when the hovered group or word changes and hidden when it leaves.
/// </summary>
public sealed partial class PhrasePopup : Window
{
    private const double FieldMaxWidth = 360;
    private const double FieldMaxHeight = 360;
    private const double Pad = 12;

    private readonly IStringLocalizer? _localizer;
    private readonly ILogger? _logger;

    public PhrasePopup(IStringLocalizer? localizer = null, ILogger? logger = null)
    {
        InitializeComponent();
        _localizer = localizer;
        _logger = logger;
        SourceInitialized += (_, _) =>
        {
            SetClickThrough();
            var handle = new WindowInteropHelper(this).Handle;
            OverlayWindowRegistry.Register(handle);
            // WPF resets WindowInteropHelper.Handle before raising Closed.
            Closed += (_, _) => OverlayWindowRegistry.Unregister(handle);
        };
    }

    public void ShowResult(
        string label, string meaning, string grammar, ScreenRect anchor,
        IReadOnlyList<WordMeaningView>? memberMeanings = null)
    {
        DataContext = PhrasePopupViewModel.ForPhrase(label, meaning, grammar, memberMeanings, _localizer);
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
        if (IsVisible)
            Hide();
    }

    private void ShowAndPosition(ScreenRect anchor)
    {
        // Realize templates and their deferred bindings before reading the content size.
        ContentPanel.Measure(new Size(FieldMaxWidth, FieldMaxHeight));
        ContentPanel.UpdateLayout();
        ContentPanel.Measure(new Size(FieldMaxWidth, FieldMaxHeight));
        Width = Math.Min(FieldMaxWidth, Math.Max(200, ContentPanel.DesiredSize.Width + Pad * 2));
        Height = Math.Min(FieldMaxHeight, ContentPanel.DesiredSize.Height + Pad * 2);
        if (!IsVisible)
            Show();
        Position(anchor);
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
            if (!Native.GetWindowRect(handle, out var window))
            {
                _logger?.LogWarning("[OVERLAY] Popup window bounds are unavailable: error={0}.", Marshal.GetLastWin32Error());
                HidePopup();
                return;
            }
            var gap = (int)Math.Ceiling(4 * (dpi == 0 ? 1 : dpi / 96d));
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

    private void SetClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64();
        Native.SetWindowLongPtr(handle, Native.GwlExStyle,
            new nint(style | Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow));
    }

    private static class Native
    {
        public const int GwlExStyle = -20;
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

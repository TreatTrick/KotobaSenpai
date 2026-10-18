using KotobaSenpai.Core.PhraseAnalysis;
using KotobaSenpai.Core.Geometry;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using System.Runtime.InteropServices;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// Detail popup for both phrase groups and LLM word meanings: shows the group's label/meaning/grammar (plus per-word
/// meanings) or a single word's headword/pos/reading/meaning/grammar. Click-through and non-activating; updated by
/// the overlay when the hovered group or word changes and hidden when it leaves.
/// </summary>
public sealed class PhrasePopup : Window
{
    private const double FieldMaxWidth = 360;
    private const double FieldMaxHeight = 360;
    private const double Pad = 12;

    private readonly StackPanel _panel = new();
    private readonly IStringLocalizer? _localizer;
    private readonly ILogger? _logger;

    public PhrasePopup(IStringLocalizer? localizer = null, ILogger? logger = null)
    {
        _localizer = localizer;
        _logger = logger;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1B, 0x1B));
        ShowInTaskbar = false;
        Topmost = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Content = new ScrollViewer
        {
            MaxHeight = FieldMaxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _panel,
        };
        SourceInitialized += (_, _) =>
        {
            SetClickThrough();
            OverlayWindowRegistry.Register(new WindowInteropHelper(this).Handle);
        };
        Closed += (_, _) => OverlayWindowRegistry.Unregister(new WindowInteropHelper(this).Handle);
    }

    public void ShowResult(
        string label, string meaning, string grammar, ScreenRect anchor,
        IReadOnlyList<WordMeaningView>? memberMeanings = null)
    {
        _panel.Children.Clear();
        _panel.Children.Add(Heading(label));
        if (!string.IsNullOrEmpty(meaning))
            _panel.Children.Add(Block("Meaning", meaning));
        if (!string.IsNullOrEmpty(grammar))
            _panel.Children.Add(Block("Grammar", grammar));
        if (memberMeanings is { Count: > 0 })
            _panel.Children.Add(Block(_localizer?.Get("Llm.WordsLabel") ?? "Words", RenderWords(memberMeanings)));
        ShowAndPosition(anchor);
    }

    private UIElement RenderWords(IReadOnlyList<WordMeaningView> meanings)
    {
        var panel = new StackPanel();
        foreach (var m in meanings)
        {
            var entry = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new TextBlock
            {
                Text = m.Headword,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            });
            if (!string.IsNullOrEmpty(m.Reading))
                header.Children.Add(new TextBlock
                {
                    Text = $" [{Kana.ToHiragana(m.Reading)}]",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xD8, 0xFF)),
                });
            header.Children.Add(new TextBlock
            {
                Text = $" ({PitchText(m)})",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xFF, 0x88)),
            });
            if (!string.IsNullOrEmpty(m.Pos))
                header.Children.Add(new TextBlock
                {
                    Text = $" {m.Pos}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xC0, 0xFF)),
                });
            entry.Children.Add(header);
            if (!string.IsNullOrEmpty(m.Meaning))
                entry.Children.Add(new TextBlock
                {
                    Text = m.Meaning,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                });
            panel.Children.Add(entry);
        }
        return panel;
    }

    /// <summary>Shows a single word's LLM meaning: headword + pos + reading + meaning + grammar.</summary>
    public void ShowWordMeaning(WordMeaningView meaning, ScreenRect anchor)
    {
        _panel.Children.Clear();
        _panel.Children.Add(Heading($"{meaning.Headword} [{Kana.ToHiragana(meaning.Reading)}]"));
        if (!string.IsNullOrEmpty(meaning.Pos))
            _panel.Children.Add(Block(_localizer?.Get("Llm.WordPosLabel") ?? "Pos", meaning.Pos));
        _panel.Children.Add(Block(_localizer?.Get("Llm.WordPitchLabel") ?? "Pitch", PitchText(meaning)));
        if (!string.IsNullOrEmpty(meaning.Meaning))
            _panel.Children.Add(Block(_localizer?.Get("Llm.WordMeaningLabel") ?? "Meaning", meaning.Meaning));
        if (!string.IsNullOrEmpty(meaning.Grammar))
            _panel.Children.Add(Block("Grammar", meaning.Grammar));
        ShowAndPosition(anchor);
    }

    /// <summary>Shows a word that has no LLM meaning: headword + reading + a "no meaning" hint.</summary>
    public void ShowWordWithoutMeaning(WordMeaningView meaning, ScreenRect anchor)
    {
        _panel.Children.Clear();
        _panel.Children.Add(Heading($"{meaning.Headword} [{Kana.ToHiragana(meaning.Reading)}]"));
        _panel.Children.Add(Block(_localizer?.Get("Llm.WordPitchLabel") ?? "Pitch", PitchText(meaning)));
        _panel.Children.Add(new TextBlock
        {
            Text = _localizer?.Get("Llm.WordNoMeaning") ?? "no meaning",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        ShowAndPosition(anchor);
    }

    private string PitchText(WordMeaningView meaning)
        => string.IsNullOrEmpty(meaning.PitchAccentText)
            ? _localizer?.Get("Llm.WordPitchUnknown") ?? "pitch unknown"
            : meaning.PitchAccentText;

    public void HidePopup()
    {
        if (IsVisible)
            Hide();
    }

    private void ShowAndPosition(ScreenRect anchor)
    {
        _panel.Measure(new Size(FieldMaxWidth, FieldMaxHeight));
        Width = Math.Min(FieldMaxWidth, Math.Max(200, _panel.DesiredSize.Width + Pad * 2));
        Height = Math.Min(FieldMaxHeight, _panel.DesiredSize.Height + Pad * 2);
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

    private static UIElement Heading(string text)
        => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };

    private static UIElement Block(string title, string text)
        => Block(title, new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        });

    private static UIElement Block(string title, UIElement content)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        block.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xD8, 0xFF)),
            Margin = new Thickness(0, 0, 0, 2),
        });
        block.Children.Add(content);
        return block;
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

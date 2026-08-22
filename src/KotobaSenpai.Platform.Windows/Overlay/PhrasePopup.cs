using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Models;

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

    public PhrasePopup(IStringLocalizer? localizer = null)
    {
        _localizer = localizer;
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
        SourceInitialized += (_, _) => SetClickThrough();
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
        Position(anchor);
        if (!IsVisible)
            Show();
    }

    private void Position(ScreenRect anchor)
    {
        var wa = SystemParameters.WorkArea;
        double x = anchor.X;
        double y = anchor.Bottom + 4;
        if (y + Height > wa.Bottom)
            y = Math.Max(wa.Top, anchor.Y - Height - 4);
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
        Left = x;
        Top = y;
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

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern nint GetWindowLongPtr(nint hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
    }
}

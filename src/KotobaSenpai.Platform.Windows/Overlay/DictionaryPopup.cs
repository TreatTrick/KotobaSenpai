using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// 本地词典释义弹窗：显示在悬停词旁的只读小窗。点击穿透（不拦截下方窗口点击）、
/// 不激活、置顶；由覆盖层在悬停词变化时更新、移出时隐藏。
/// </summary>
public sealed class DictionaryPopup : Window
{
    private const double FieldMaxWidth = 320;
    private const double FieldMaxHeight = 320;
    private const double Pad = 12;

    private readonly StackPanel _panel = new();

    public DictionaryPopup()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1B, 0x1B, 0x1B));
        ShowInTaskbar = false;
        Topmost = true;
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

    /// <summary>按查词结果重建内容并定位到词旁；无结果时显示读音与"未收录"。</summary>
    public void ShowResult(IReadOnlyList<DictionaryEntry> entries, string reading, ScreenRect wordBounds)
    {
        _panel.Children.Clear();
        if (entries.Count == 0)
            _panel.Children.Add(NotFound(reading));
        else
            foreach (var entry in entries)
                _panel.Children.Add(RenderEntry(entry));

        _panel.Measure(new Size(FieldMaxWidth, FieldMaxHeight));
        Width = Math.Min(FieldMaxWidth, Math.Max(180, _panel.DesiredSize.Width + Pad * 2));
        Height = Math.Min(FieldMaxHeight, _panel.DesiredSize.Height + Pad * 2);
        Position(wordBounds);
        if (!IsVisible)
            Show();
    }

    public void HidePopup()
    {
        if (IsVisible)
            Hide();
    }

    /// <summary>定位在词包围盒下方；贴屏幕底/右时翻到词上方并钳制到工作区（仅主屏，多屏待后续）。</summary>
    private void Position(ScreenRect wordBounds)
    {
        var wa = SystemParameters.WorkArea;
        double x = wordBounds.X;
        double y = wordBounds.Bottom + 4;
        if (y + Height > wa.Bottom)
            y = Math.Max(wa.Top, wordBounds.Y - Height - 4);
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
        Left = x;
        Top = y;
    }

    private static UIElement NotFound(string reading)
        => new TextBlock
        {
            Text = $"[{reading}]  (not found)",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        };

    private static UIElement RenderEntry(DictionaryEntry entry)
    {
        var card = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = entry.Headword,
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            Foreground = Brushes.White,
        });
        if (!string.IsNullOrEmpty(entry.Reading))
            header.Children.Add(new TextBlock
            {
                Text = $"[{entry.Reading}]",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xD8, 0xFF)),
            });
        card.Children.Add(header);

        for (int i = 0; i < entry.Senses.Count; i++)
        {
            var sense = entry.Senses[i];
            var pos = sense.Pos.Count > 0 ? string.Join(", ", sense.Pos) + ": " : "";
            card.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {pos}{string.Join("; ", sense.Glosses)}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        return card;
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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// phrase group 详情弹窗：显示标签、中文意思与中文语法解释。点击穿透、不激活、置顶；
/// 由覆盖层在悬停 group 变化时更新、移出时隐藏。
/// </summary>
public sealed class PhrasePopup : Window
{
    private const double FieldMaxWidth = 360;
    private const double FieldMaxHeight = 360;
    private const double Pad = 12;

    private readonly StackPanel _panel = new();

    public PhrasePopup()
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

    public void ShowResult(string label, string meaning, string grammar, ScreenRect anchor)
    {
        _panel.Children.Clear();
        _panel.Children.Add(Heading(label));
        if (!string.IsNullOrEmpty(meaning))
            _panel.Children.Add(Block("意思", meaning));
        if (!string.IsNullOrEmpty(grammar))
            _panel.Children.Add(Block("语法", grammar));

        _panel.Measure(new Size(FieldMaxWidth, FieldMaxHeight));
        Width = Math.Min(FieldMaxWidth, Math.Max(200, _panel.DesiredSize.Width + Pad * 2));
        Height = Math.Min(FieldMaxHeight, _panel.DesiredSize.Height + Pad * 2);
        Position(anchor);
        if (!IsVisible)
            Show();
    }

    public void HidePopup()
    {
        if (IsVisible)
            Hide();
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
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        block.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0xD8, 0xFF)),
            Margin = new Thickness(0, 0, 0, 2),
        });
        block.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        });
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
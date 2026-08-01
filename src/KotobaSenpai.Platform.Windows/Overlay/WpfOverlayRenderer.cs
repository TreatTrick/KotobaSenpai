using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KotobaSenpai.Core.Contracts;
using KotobaSenpai.Core.Models;

namespace KotobaSenpai.Platform.Windows.Overlay;

/// <summary>
/// 端口 <see cref="IOverlayRenderer"/> 的 WPF 实现：
/// 透明、置顶、不激活、点击穿透的工具窗口，为每个有效词绘制一条下划线。
/// 刷新时整体替换词列表，隐藏时清除所有横线。
/// </summary>
public sealed class WpfOverlayRenderer : IOverlayRenderer
{
    private OverlayWindow? _window;

    public void Show(WordOverlaySession session)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window ??= new OverlayWindow();
            _window.Render(session);
        });
    }

    public void Hide()
    {
        Application.Current.Dispatcher.Invoke(() => _window?.Hide());
    }

    private sealed class OverlayWindow : Window
    {
        private readonly Canvas _canvas = new();

        public OverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            IsHitTestVisible = false;
            Content = _canvas;
            SourceInitialized += (_, _) => SetClickThrough();
        }

        public void Render(WordOverlaySession session)
        {
            if (!IsVisible)
                Show();

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
            foreach (var line in session.Lines)
            {
                var element = new Border
                {
                    Width = line.Width / scale,
                    Height = line.Thickness / scale,
                    Background = Brushes.DeepSkyBlue,
                    Opacity = 0.95
                };
                Canvas.SetLeft(element, (line.X - session.Target.Bounds.X) / scale);
                Canvas.SetTop(element, (line.Y - session.Target.Bounds.Y) / scale);
                _canvas.Children.Add(element);
            }
        }

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
    }
}

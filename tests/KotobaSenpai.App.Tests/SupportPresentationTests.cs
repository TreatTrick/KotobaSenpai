using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Support;
using KotobaSenpai.App.Views;
using KotobaSenpai.App.Views.Support;
using KotobaSenpai.Core.Logging;
using Wpf.Ui.Appearance;

namespace KotobaSenpai.App.Tests;

public sealed partial class PhrasePopupThemeTests
{
    private static void CheckSupportPresentation(Application application)
    {
        var localizer = new ResourceManagerStringLocalizer();
        var previousLocalizer = LocalizationHost.Localizer;
        var previousCulture = CultureInfo.CurrentUICulture;
        LocalizationHost.Localizer = localizer;
        using var support = new SupportService(new MemorySettings(), localizer, new SupportLogger());
        try
        {
            foreach (var culture in new[] { "en", "zh-CN" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                localizer.ApplyCulture(CultureInfo.CurrentUICulture);
                var popup = new SupportWindow(support, localizer, new SupportLogger())
                { Left = -10000, Top = -10000, ShowActivated = false };
                try
                {
                    popup.Show();
                    PumpSupportFrames();
                    Assert.NotNull(Assert.IsType<Image>(popup.FindName("QrImage")).Source);
                    Assert.Equal(localizer.Get("support.usage", 0), Assert.IsType<TextBlock>(popup.FindName("UsageText")).Text);
                    Assert.True(Assert.IsType<RadioButton>(popup.FindName(culture == "en" ? "PayPal" : "WeChat")).IsChecked);
                    Assert.Equal(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast,
                        Assert.IsType<MascotControl>(popup.FindName("Mascot")).IsRendering);
                    foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
                    {
                        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, false);
                        popup.UpdateLayout();
                        Assert.True(popup.ActualHeight < 620);
                        SaveSupportPreview(popup, $"support-{culture}-{theme}");
                    }
                    var before = popup.ActualHeight;
                    var confirm = Assert.IsType<Button>(popup.FindName("ConfirmButton"));
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    popup.UpdateLayout();
                    Assert.True(support.IsSupported);
                    Assert.Equal(before, popup.ActualHeight);
                    Assert.Equal(Visibility.Visible, Assert.IsType<Grid>(popup.FindName("ThanksCard")).Visibility);
                    SaveSupportPreview(popup, $"support-thanks-{culture}");
                    var selection = Assert.IsType<RadioButton>(popup.FindName("WeChat"));
                    selection.IsChecked = true;
                    localizer.ApplyCulture(CultureInfo.GetCultureInfo(culture == "en" ? "zh-CN" : "en"));
                    Assert.True(selection.IsChecked);
                    Assert.Equal(localizer.Get("support.thanks.title"), Assert.IsType<TextBlock>(popup.FindName("HeaderTitle")).Text);
#if DEBUG
                    Assert.True(support.ResetForDebugging());
                    Assert.False(Assert.IsType<MascotControl>(popup.FindName("Mascot")).IsSupported);
#endif
                    var mascot = Assert.IsType<MascotControl>(popup.FindName("Mascot"));
                    popup.Close();
                    Assert.False(mascot.IsRendering);
                }
                finally { popup.Close(); }
                localizer.ApplyCulture(CultureInfo.CurrentUICulture);
                foreach (var size in new[] { new Size(900, 572), new Size(1100, 752) })
                {
                    var main = new MainContentView { DataContext = new { Support = support } };
                    var host = new Window { Content = main, Width = size.Width, Height = size.Height,
                        Left = -10000, Top = -10000, ShowActivated = false, WindowStyle = WindowStyle.None };
                    try
                    {
                        host.Show();
                        host.UpdateLayout();
                        PumpSupportFrames();
                        var companion = Assert.Single(Descendants(main).OfType<SupportCompanion>());
                        var button = Assert.IsType<Button>(companion.FindName("CoffeeButton"));
                        var mascot = Assert.Single(Descendants(companion).OfType<MascotControl>());
                        Assert.True(button.IsVisible && button.ActualHeight > 20);
                        Assert.True(mascot.ActualHeight > 0);
                        var buttonBounds = button.TransformToAncestor(main).TransformBounds(new Rect(button.RenderSize));
                        Assert.True(buttonBounds.Bottom <= main.ActualHeight);
                        SaveSupportPreview(main, $"companion-{culture}-{size.Width}");
                        if (culture == "en" && size.Width == 900)
                        {
                            Assert.True(host.Activate());
                            PumpSupportFrames();
                            var activePopup = new SupportWindow(support, localizer, new SupportLogger())
                            { Owner = host, Left = -10000, Top = -10000, ShowActivated = true };
                            try
                            {
                                activePopup.ShowSupport(host, false);
                                Assert.False(activePopup.IsActive);
                                Assert.True(host.IsActive);
                                Assert.InRange(activePopup.Opacity, 0, 1);
                                Assert.True(activePopup.Activate());
                                PumpSupportFrames(400);
                                Assert.Equal(1, activePopup.Opacity);
                                Assert.False(host.IsActive);
                                Assert.Equal(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast, mascot.IsRendering);
                                Assert.True(support.ConfirmSupport());
                                Assert.True(mascot.IsSupported);
                                Assert.Equal(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast, mascot.IsRendering);
                                activePopup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                                    PresentationSource.FromVisual(activePopup), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                                PumpSupportFrames(250);
                                Assert.False(activePopup.IsVisible);
                            }
                            finally { activePopup.Close(); }
                        }
                        host.WindowState = WindowState.Minimized;
                        Assert.False(mascot.IsRendering);
                        host.Close();
                        Assert.False(mascot.IsRendering);
                    }
                    finally { host.Close(); }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            LocalizationHost.Localizer = previousLocalizer;
        }
    }

    private static void PumpSupportFrames(int milliseconds = 200)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void SaveSupportPreview(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, "previews");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    private sealed class SupportLogger : ILogger
    {
        public void Log(LogLevel level, Exception? exception, string message, params object[] args)
        {
            if (level == LogLevel.Error) throw new InvalidOperationException(message, exception);
        }
    }
}

public sealed class MascotFramesTests
{
    [Fact]
    public async Task Full_size_decoded_frames_are_frozen_shared_and_bounded()
    {
        var cache = new MascotFrames();
        var requests = await Task.WhenAll(cache.GetAsync("A/000.png"), cache.GetAsync("A/000.png"));
        Assert.Same(requests[0], requests[1]);
        Assert.True(requests[0].IsFrozen);
        Assert.Equal(249, requests[0].PixelWidth);
        Assert.Equal(560, requests[0].PixelHeight);
        for (var index = 1; index < 50; index++) await cache.GetAsync($"A/{index:000}.png");
        Assert.Equal(MascotFrames.Capacity, cache.Count);
        await cache.GetAsync("B/000.png");
        Assert.Equal(MascotFrames.Capacity, cache.Count);
    }
}

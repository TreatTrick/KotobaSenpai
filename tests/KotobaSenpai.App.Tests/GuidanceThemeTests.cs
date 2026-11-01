using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KotobaSenpai.App.Guidance;
using KotobaSenpai.App.Guidance.Presentation;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.Views;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Platform.Windows.Guidance;
using KotobaSenpai.Platform.Windows.Overlay;
using KotobaSenpai.Platform.Windows.Presentation;
using KotobaSenpai.App.Controls;
using Wpf.Ui.Appearance;

namespace KotobaSenpai.App.Tests;

internal static class GuidanceDesktopFixture
{
    internal const string DirectoryEnvironmentVariable = "KOTOBA_GUIDANCE_FIXTURE_DIRECTORY";
    internal const string FailureFileName = "failure.txt";

    internal static int Run(string directory)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            GuidancePresentationTests.CheckSmallViewport(84, 100);
            application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = ApplicationTheme.Light });
            application.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            foreach (var path in new[]
            {
                "/KotobaSenpai.Platform.Windows;component/Presentation/FluentDesignTokens.xaml",
                "/KotobaSenpai.App;component/Themes/SettingsStyles.xaml",
                "/KotobaSenpai.App;component/Themes/FluentColorIcons.xaml",
            }) application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
            foreach (var key in typeof(ThemeResourceKeys).GetFields())
                Assert.IsAssignableFrom<Brush>(application.FindResource(key.GetRawConstantValue()!));
            GuidancePresentationTests.CheckGuideLight(false, directory);
            GuidancePresentationTests.CheckGuideLight(true, directory);
            GuidancePresentationTests.CheckSmallViewport(36, 80);
            GuidancePresentationTests.CheckSmallViewport(84, 100);
            GuidancePresentationTests.CheckSmallViewport(300, 140);
            GuidancePresentationTests.CheckMainInput();
            GuidancePresentationTests.CheckRegionLifecycle();
            CheckGuidancePresentation(application, directory);
            return 0;
        }
        catch (Exception exception) { File.WriteAllText(Path.Combine(directory, FailureFileName), exception.ToString()); return 1; }
        finally { application.Shutdown(); }
    }

    private static void CheckGuidancePresentation(Application application, string directory)
    {
        var localizer = new ResourceManagerStringLocalizer();
        var previousLocalizer = LocalizationHost.Localizer;
        LocalizationHost.Localizer = localizer;
        try
        {
            foreach (var culture in new[] { "en", "zh-CN" })
            foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
            {
                localizer.ApplyCulture(CultureInfo.GetCultureInfo(culture));
                ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, false);
                foreach (var width in new[] { 900, 1100 })
                {
                    var guidance = new ContextualGuidance(new GuidanceTests.MemorySettings(), new GuidanceTests.TestLogger());
                    var main = new MainContentView
                    {
                        DataContext = new
                        {
                            Recognition = new
                            {
                                Selection = new
                                {
                                    SelectionButtonText = localizer.Get(ResourceKeys.WindowPicker_Select),
                                    TargetTitle = localizer.Get(ResourceKeys.Status_SelectTarget),
                                    TargetStatus = localizer.Get(ResourceKeys.WindowPicker_EmptyHint),
                                },
                            },
                        },
                    };
                    var root = Assert.IsType<Grid>(main.FindName(nameof(MainContentView.ContentRoot)));
                    var navigation = Assert.IsType<TabControl>(main.FindName(nameof(MainContentView.MainNavigation)));
                    var page = Assert.IsType<RecognitionPage>(main.FindName(nameof(MainContentView.RecognitionView)));
                    var host = new Window { Content = main, Width = width, Height = width == 900 ? 572 : 752,
                        Left = -10000, Top = -10000, ShowActivated = false, WindowStyle = WindowStyle.None, Style = null };
                    try
                    {
                        host.Show();
                        host.UpdateLayout();
                        using var presenter = new MainInterfaceTourPresenter(root, navigation, page.FindGuideTarget,
                            new TourPresentation(guidance.Main, localizer));
                        for (var step = 0; step < 4; step++)
                        {
                            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                            host.UpdateLayout();
                            var current = guidance.Main.Current!;
                            var anchor = page.FindGuideTarget(current.Target)!;
                            var bounds = anchor.TransformToAncestor(root).TransformBounds(new Rect(anchor.RenderSize));
                            Assert.True(bounds.Top >= 0 && bounds.Bottom <= root.ActualHeight, "The anchor must be visible after scrolling.");
                            var overlay = Assert.Single(root.Children.OfType<GuidanceOverlay>());
                            var buttonBounds = overlay.AcknowledgeButton.TransformToAncestor(root)
                                .TransformBounds(new Rect(overlay.AcknowledgeButton.RenderSize));
                            Assert.True(new Rect(root.RenderSize).Contains(buttonBounds), "The acknowledgement must fit inside the main window.");
                            Assert.Same(application.FindResource(ThemeResourceKeys.SolidBackgroundFillColorBaseBrush),
                                Assert.IsType<Border>(Assert.Single(overlay.Children.OfType<FluentLightHost>()).Child).Background);
                            SavePreview(main, directory, $"main-{culture}-{theme}-{width}-{step + 1}");
                            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(host), step, Key.Enter)
                                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                            overlay.RaiseEvent(key);
                            Assert.True(key.Handled);
                        }
                        Assert.True(navigation.IsEnabled);
                        Assert.False(guidance.IsActive);
                    }
                    finally { host.Close(); }
                }

                var regionGuidance = new ContextualGuidance(new GuidanceTests.MemorySettings(), new GuidanceTests.TestLogger());
                var targetHost = new Window { Width = 800, Height = 450, Left = -10000, Top = -10000,
                    ShowActivated = false, WindowStyle = WindowStyle.None, Style = null };
                targetHost.Show();
                var target = new WindowTarget(new System.Windows.Interop.WindowInteropHelper(targetHost).Handle,
                    "Guidance target", new ScreenRect(-10000, -10000, 800, 450));
                var selector = new RegionSelectorWindow(new GuidanceTests.MemorySettings(), localizer,
                    guidance: new TourPresentation(regionGuidance.Region, localizer),
                    decorateBubble: (bubble, button) => GuidanceLight.DecorateBubble(bubble, button, reactToPointerWhenInactive: true));
                try
                {
                    selector.Show(target);
                    for (var step = 0; step < 2; step++)
                    {
                        selector.UpdateLayout();
                        var overlay = Assert.Single(((Canvas)selector.Content).Children.OfType<GuidanceOverlay>());
                        var buttonBounds = overlay.AcknowledgeButton.TransformToAncestor(selector)
                            .TransformBounds(new Rect(overlay.AcknowledgeButton.RenderSize));
                        Assert.True(new Rect(selector.RenderSize).Contains(buttonBounds), "The acknowledgement must fit inside the target window.");
                        var bubble = Assert.IsType<Border>(Assert.Single(overlay.Children.OfType<FluentLightHost>()).Child);
                        var bubbleBounds = bubble.TransformToAncestor(selector).TransformBounds(new Rect(bubble.RenderSize));
                        var confirm = Assert.Single(((Canvas)selector.Content).Children.OfType<Button>());
                        var confirmBounds = confirm.TransformToAncestor(selector).TransformBounds(new Rect(confirm.RenderSize));
                        if (step == 1) Assert.False(bubbleBounds.IntersectsWith(confirmBounds), "The explanation must leave its confirmation target visible.");
                        SavePreview(selector, directory, $"region-{culture}-{theme}-{step + 1}");
                        overlay.AcknowledgeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    Assert.True(selector.IsVisible);
                    Assert.False(regionGuidance.IsActive);
                }
                finally { selector.Close(); targetHost.Close(); }
            }
        }
        finally { LocalizationHost.Localizer = previousLocalizer; }
    }

    internal static void SavePreview(FrameworkElement element, string directory, string name)
    {
        foreach (var scale in new[] { 1d, 1.5 })
        {
            var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale), (int)(element.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"{name}-{scale * 100}.png"));
            encoder.Save(file);
        }
    }
}

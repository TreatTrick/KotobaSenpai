using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using KotobaSenpai.App.Controls;

namespace KotobaSenpai.App.Tests;

public sealed class FluentLightCoverageTests
{
    [Fact]
    public Task Shortcut_capture_and_readonly_hints_inherit_the_shared_presentation() => RunSta(() =>
    {
        var resources = LoadResources("Light");
        var button = new ShortcutCaptureButton { Style = (Style)resources[typeof(ShortcutCaptureButton)] };
        Assert.Equal(FluentLightKind.Button, FluentLight.GetKind(button));
        Assert.Equal(4, FluentLight.GetCornerRadius(button));
        Assert.Equal(new CornerRadius(4), button.CornerRadius);
        var hint = new ContentControl { Style = (Style)resources["ShortcutHint"], Content = new { GestureText = "Ctrl+Alt+Shift+F11" } };
        Assert.False(hint.Focusable);
        Assert.False(hint.IsHitTestVisible);
        Assert.Equal(Visibility.Visible, hint.Visibility);
        hint.Content = new { GestureText = "" };
        Assert.Equal(Visibility.Collapsed, hint.Visibility);
    });

    [Fact]
    public void All_five_actions_show_the_live_binding_beside_their_control()
    {
        var page = Source("Views/RecognitionPage.xaml");
        foreach (var action in new[] { "Recognize", "Automatic", "Stop", "Visibility", "Region" })
        {
            var expression = "{Binding Shortcuts." + action + ".Hint}";
            Assert.Single(page.Descendants(Presentation + "ContentControl"), element =>
                (string?)element.Attribute("Content") == "{Binding Shortcuts." + action + "}");
            Assert.Single(page.Descendants(), element => (string?)element.Attribute("AutomationProperties.AcceleratorKey") == expression);
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task Shortcut_keycaps_show_modifiers_and_the_inactive_state(string theme) => RunSta(() =>
    {
        var resources = LoadResources(theme);
        var hint = new ContentControl
        {
            Style = (Style)resources["ShortcutHint"],
            Content = new { GestureText = "Ctrl+Alt+Shift+F11", ShortcutLabel = "Shortcut",
                Keys = new[] { new { Text = "Ctrl", HasSeparator = false }, new { Text = "Alt", HasSeparator = true },
                    new { Text = "Shift", HasSeparator = true }, new { Text = "F11", HasSeparator = true } }, InactiveText = "Inactive" },
        };
        var frame = new Border { Background = (Brush)resources["SolidBackgroundFillColorBaseBrush"], Child = hint };
        var window = new Window
        {
            Resources = resources, Content = frame, Width = 300, Height = 120,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Left = -10000, Top = -10000,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var settled = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => { timer.Stop(); settled.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(settled);
            var visuals = VisualDescendants(hint).ToArray();
            var caps = visuals.OfType<Border>().Where(element => FluentLight.GetKind(element) == FluentLightKind.Surface).ToArray();
            Assert.Equal(4, caps.Length);
            Assert.All(caps, cap =>
            {
                Assert.Equal(new CornerRadius(4), cap.CornerRadius);
                Assert.Equal(4, FluentLight.GetCornerRadius(cap));
                Assert.True(cap.ActualWidth > 0);
                Assert.False(cap.IsHitTestVisible);
                Assert.True(cap.TranslatePoint(new Point(cap.ActualWidth, cap.ActualHeight), hint).X <= hint.ActualWidth + 0.5);
                Assert.True(cap.TranslatePoint(new Point(cap.ActualWidth, cap.ActualHeight), hint).Y <= hint.ActualHeight + 0.5);
            });
            Assert.Equal(3, visuals.OfType<TextBlock>().Count(text => text.Text == "+" && text.Visibility == Visibility.Visible));
            Assert.Contains(visuals.OfType<TextBlock>(), text => text.Text == "Shortcut" && text.IsVisible);
            Assert.Contains(visuals.OfType<TextBlock>(), text => text.Text == "Inactive" && text.IsVisible);
            var width = (int)Math.Ceiling(frame.ActualWidth);
            var height = (int)Math.Ceiling(frame.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(frame);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            var colors = Enumerable.Range(0, width * height).Select(index => BitConverter.ToUInt32(pixels, index * 4)).Distinct().Count();
            Assert.True(colors > 1, $"No visible keycaps: hint={hint.RenderSize}, position={hint.TransformToAncestor(frame).Transform(new Point())}, frame={frame.RenderSize}.");
            var directory = Path.Combine(AppContext.BaseDirectory, "previews");
            Directory.CreateDirectory(directory);
            using var file = File.Create(Path.Combine(directory, $"keycap-{theme}.png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
        }
        finally { window.Close(); }
    });

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Controls = "clr-namespace:KotobaSenpai.App.Controls";
    private static readonly XNamespace Views = "clr-namespace:KotobaSenpai.App.Views";

    [Fact]
    public void Fetch_models_uses_a_filled_accent_button_with_the_configuration_checks_corner_style()
    {
        var button = Assert.Single(Source("Views/OnboardingModelView.xaml").Descendants(),
            element => (string?)element.Attribute("Command") == "{Binding FetchModelsCommand}");
        Assert.Equal("{StaticResource OnboardingButton}", (string?)button.Attribute("Style"));
        Assert.Equal("{DynamicResource AccentFillColorDefaultBrush}", (string?)button.Attribute("Background"));
        Assert.Equal("{DynamicResource TextOnAccentFillColorPrimaryBrush}", (string?)button.Attribute("Foreground"));
    }

    [Fact]
    public void Onboarding_leaves_the_shell_mica_background_visible()
    {
        Assert.Equal("Mica", (string?)Source("MainWindow.xaml").Root!.Attribute("WindowBackdropType"));
        Assert.Equal("Transparent", (string?)Source("Views/OnboardingPage.xaml").Root!.Attribute("Background"));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task The_content_layer_is_translucent_in_both_themes(string theme) => RunSta(() =>
    {
        var brush = Assert.IsType<SolidColorBrush>(LoadResources(theme)["LayerFillColorDefaultBrush"]);
        Assert.InRange(brush.Color.A * brush.Opacity, 1, 254);
    });

    [Fact]
    public void The_shell_shares_one_light_host_between_onboarding_and_main_pages()
    {
        var shell = Source("MainWindow.xaml");
        var host = Assert.Single(shell.Descendants(Controls + nameof(FluentLightHost)));
        var content = Assert.Single(host.Elements(Presentation + nameof(ContentControl)));
        Assert.Single(content.Descendants(Views + "OnboardingPage"));
        Assert.Single(content.Descendants(Views + "MainContentView"));
        Assert.Empty(Source("Views/MainContentView.xaml").Descendants(Controls + nameof(FluentLightHost)));
    }

    [Theory]
    [InlineData("Views/Support/SupportWindow.xaml", "Window")]
    [InlineData("Controls/OverlayColorPicker.xaml", "Popup")]
    public void Separate_visual_roots_have_their_own_light_host(string path, string rootType)
    {
        var document = Source(path);
        var root = rootType == "Window" ? document.Root! : Assert.Single(document.Descendants(Presentation + rootType));
        Assert.Single(root.Elements(Controls + nameof(FluentLightHost)));
    }

    [Theory]
    [InlineData("OnboardingButton", FluentLightKind.Button, 8)]
    [InlineData("OnboardingPrimaryButton", FluentLightKind.Button, 23)]
    [InlineData("OnboardingLinkButton", FluentLightKind.Button, 8)]
    [InlineData("OnboardingTab", FluentLightKind.Button, 6)]
    [InlineData("OnboardingStepBadge", FluentLightKind.Surface, 14)]
    [InlineData("OnboardingTextInput", FluentLightKind.Surface, 4)]
    [InlineData("OnboardingComboInput", FluentLightKind.Button, 4)]
    [InlineData("MaskedApiKeyInput", FluentLightKind.Surface, 4)]
    [InlineData("SettingInput", FluentLightKind.Button, 4)]
    [InlineData("SettingTextInput", FluentLightKind.Surface, 4)]
    [InlineData("SupportButton", FluentLightKind.Button, 6)]
    public Task Shared_styles_supply_light_feedback_and_match_control_corners(string key, FluentLightKind kind, double radius)
        => RunSta(() =>
        {
            var style = Assert.IsType<Style>(LoadResources("Light")[key]);
            var element = Assert.IsAssignableFrom<FrameworkElement>(Activator.CreateInstance(style.TargetType));
            element.Style = style;

            Assert.Equal(kind, FluentLight.GetKind(element));
            Assert.Equal(radius, FluentLight.GetCornerRadius(element));
        });

    // Inline the project dictionaries so their static references share a scope without starting Application.
    private static ResourceDictionary LoadResources(string theme) => (ResourceDictionary)XamlReader.Parse($$"""
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:ui="clr-namespace:Wpf.Ui.Controls;assembly=Wpf.Ui"
                    xmlns:markup="clr-namespace:Wpf.Ui.Markup;assembly=Wpf.Ui"
                    xmlns:controls="clr-namespace:KotobaSenpai.App.Controls;assembly=KotobaSenpai.App"
                    xmlns:system="clr-namespace:System;assembly=System.Runtime">
                  <ResourceDictionary.MergedDictionaries>
                    <markup:ThemesDictionary Theme="{{theme}}" />
                    <markup:ControlsDictionary />
                  </ResourceDictionary.MergedDictionaries>
                  {{string.Join(Environment.NewLine, Source("../KotobaSenpai.Presentation.Wpf/Resources/FluentDesignTokens.xaml").Root!.Elements())}}
                  {{string.Join(Environment.NewLine, Source("Themes/SettingsStyles.xaml").Root!.Elements())}}
                  {{string.Join(Environment.NewLine, Source("Themes/OnboardingStyles.xaml").Root!.Elements())}}
                  {{string.Join(Environment.NewLine, Source("Views/Support/SupportWindow.xaml").Root!.Element(Presentation + "Window.Resources")!.Elements())}}
                </ResourceDictionary>
                """);

    private static XDocument Source(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "KotobaSenpai.App", path);
            if (File.Exists(candidate)) return XDocument.Load(candidate);
        }
        throw new FileNotFoundException("Application XAML source was not found.", path);
    }

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}

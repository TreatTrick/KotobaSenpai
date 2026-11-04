using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
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
        var hint = new TextBlock { Style = (Style)resources["ShortcutHint"], Text = "Ctrl+Alt+Shift+F11" };
        Assert.False(hint.Focusable);
        Assert.Equal(Visibility.Visible, hint.Visibility);
        hint.Text = "";
        Assert.Equal(Visibility.Collapsed, hint.Visibility);
    });

    [Fact]
    public void All_five_actions_show_the_live_binding_beside_their_control()
    {
        var page = Source("Views/RecognitionPage.xaml");
        foreach (var action in new[] { "Recognize", "Automatic", "Stop", "Visibility", "Region" })
        {
            var expression = "{Binding Shortcuts." + action + ".Hint}";
            Assert.Single(page.Descendants(Presentation + "TextBlock"), element => (string?)element.Attribute("Text") == expression);
            Assert.Single(page.Descendants(), element => (string?)element.Attribute("AutomationProperties.AcceleratorKey") == expression);
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
                  {{string.Join(Environment.NewLine, Source("../KotobaSenpai.Platform.Windows/Presentation/FluentDesignTokens.xaml").Root!.Elements())}}
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

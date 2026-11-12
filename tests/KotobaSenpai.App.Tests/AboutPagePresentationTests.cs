using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KotobaSenpai.App.Controls;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.App.Views;
using KotobaSenpai.Core.Updates;
using Wpf.Ui.Appearance;

namespace KotobaSenpai.App.Tests;

public sealed partial class PhrasePopupThemeTests
{
    private static void CheckAboutPresentation(Application application)
    {
        var previousLocalization = application.Resources[LocalizationHost.ResourceKey];
        var localizer = LocalizerFactory.Create(CultureInfo.GetCultureInfo("en"));
        using var localization = new LocalizationHost(localizer);
        application.Resources[LocalizationHost.ResourceKey] = localization;
        try
        {
            foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
            foreach (var culture in new[] { "en", "zh-CN" })
            {
                localizer.ApplyCulture(CultureInfo.GetCultureInfo(culture));
                ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, false);
                var source = new AboutUpdateSource();
                using var state = new AboutPageViewModel(source, new AboutUpdateInstaller(), localizer,
                    new UserMessageResolver(localizer), ViewModelDependencies.Logger, new(1, 0, 1));
                var main = new MainContentView { DataContext = new { About = state } };
                var navigation = Assert.IsType<TabControl>(main.FindName("MainNavigation"));
                navigation.SelectedIndex = 2;
                var page = Assert.IsType<AboutPage>(navigation.SelectedContent);
                var indicator = Assert.IsType<Image>(main.FindName("AboutUpdateIndicator"));
                var light = new FluentLightHost { Child = main };
                var frame = new Border { Background = (Brush)application.FindResource("SolidBackgroundFillColorBaseBrush"), Child = light };
                var window = new Window { Content = frame, Width = 900, Height = 752, WindowStyle = WindowStyle.None,
                    Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show();
                    state.EnsureCheckedAsync().GetAwaiter().GetResult();
                    window.UpdateLayout();
                    Assert.True(indicator.IsVisible);
                    Assert.Same(application.FindResource("UpdateAvailableColorIcon"), indicator.Source);
                    foreach (var name in new[] { "AppInfoCard", "UpdateDetailsCard" })
                    {
                        var surface = Assert.IsType<Border>(page.FindName(name));
                        Assert.Equal(new CornerRadius(8), surface.CornerRadius);
                        Assert.Equal(8, FluentLight.GetCornerRadius(surface));
                        Assert.Equal(FluentLightKind.Surface, FluentLight.GetKind(surface));
                        Assert.True(light.IsAncestorOf(surface));
                        Assert.Same(application.FindResource("CardBackgroundFillColorDefaultBrush"), surface.Background);
                    }
                    foreach (var button in Descendants(page).OfType<Wpf.Ui.Controls.Button>())
                    {
                        Assert.Equal(FluentLightKind.Button, FluentLight.GetKind(button));
                        Assert.Equal(4, FluentLight.GetCornerRadius(button));
                        Assert.Equal(new CornerRadius(4), button.CornerRadius);
                    }
                    Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == localizer.Get("Tab_About"));
                    Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == state.CurrentVersionText);
                    Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == state.Status);
                    var version = Assert.IsType<TextBlock>(page.FindName("LatestVersionLabel"));
                    var download = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("DownloadUpdateButton"));
                    var release = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("OpenReleaseButton"));
                    var notes = Assert.IsType<TextBlock>(page.FindName("ReleaseNotesHeading"));
                    var updateIcon = Assert.IsType<Wpf.Ui.Controls.ImageIcon>(download.Icon);
                    Assert.Same(indicator.Source, updateIcon.Source);
                    Assert.True(version.TransformToAncestor(page).Transform(new Point(0, version.ActualHeight)).Y
                        <= download.TransformToAncestor(page).Transform(new Point()).Y);
                    Assert.True(download.TransformToAncestor(page).Transform(new Point(0, download.ActualHeight)).Y
                        < notes.TransformToAncestor(page).Transform(new Point()).Y);
                    Assert.Equal(download.TransformToAncestor(page).Transform(new Point()).Y,
                        release.TransformToAncestor(page).Transform(new Point()).Y);
                    Assert.True(download.TransformToAncestor(page).Transform(new Point(0, download.ActualHeight)).Y < page.ActualHeight,
                        "The update action must remain visible above long release notes.");
                    SaveSupportPreview(frame, $"about-{theme}-{culture}");
                    state.DownloadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    window.UpdateLayout();
                    var install = Assert.Single(Descendants(page).OfType<Wpf.Ui.Controls.Button>(), button => ReferenceEquals(button.Command, state.InstallCommand));
                    Assert.True(install.IsVisible);
                    Assert.True(install.IsEnabled);
                    Assert.True(indicator.IsVisible);
                    SaveSupportPreview(frame, $"about-ready-{theme}-{culture}");
                    source.Latest = null;
                    state.CheckCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, indicator.Visibility);
                    Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(page.FindName("UpdateDetailsCard")).Visibility);
                }
                finally { window.Close(); }
            }
        }
        finally { application.Resources[LocalizationHost.ResourceKey] = previousLocalization; }
    }

    private sealed class AboutUpdateSource : IAppUpdateSource
    {
        public AppRelease? Latest { get; set; } = new("v1.0.2", new(1, 0, 2),
            string.Join(Environment.NewLine, Enumerable.Repeat("Improved recognition and update support with preserved settings and offline dictionary preparation.", 30)),
            new("https://example.com"), 117041768, "fixture");
        public Task<AppRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
            => Task.FromResult(Latest);
        public Task<DownloadedAppUpdate> DownloadAsync(AppRelease release, IProgress<AppUpdateProgress> progress, CancellationToken cancellationToken)
            => Task.FromResult(new DownloadedAppUpdate(release, "fixture.exe"));
    }
    private sealed class AboutUpdateInstaller : IAppUpdateInstaller
    {
        public bool IsAvailable => true;
        public Task LaunchAsync(DownloadedAppUpdate update, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The presentation fixture must never start an installer.");
    }
}

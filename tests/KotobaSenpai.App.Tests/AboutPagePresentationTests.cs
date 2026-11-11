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
                using var state = new AboutPageViewModel(new AboutUpdateSource(), new AboutUpdateInstaller(), localizer,
                    new UserMessageResolver(localizer), ViewModelDependencies.Logger, new(1, 0, 1));
                state.EnsureCheckedAsync().GetAwaiter().GetResult();
                var page = new AboutPage { DataContext = state };
                var light = new FluentLightHost { Child = page };
                var frame = new Border { Background = (Brush)application.FindResource("SolidBackgroundFillColorBaseBrush"), Child = light };
                var window = new Window { Content = frame, Width = 676, Height = 752, WindowStyle = WindowStyle.None,
                    Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show();
                    window.UpdateLayout();
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
                    SaveSupportPreview(frame, $"about-{theme}-{culture}");
                    state.DownloadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    window.UpdateLayout();
                    var install = Assert.Single(Descendants(page).OfType<Wpf.Ui.Controls.Button>(), button => ReferenceEquals(button.Command, state.InstallCommand));
                    Assert.True(install.IsVisible);
                    Assert.True(install.IsEnabled);
                    SaveSupportPreview(frame, $"about-ready-{theme}-{culture}");
                }
                finally { window.Close(); }
            }
        }
        finally { application.Resources[LocalizationHost.ResourceKey] = previousLocalization; }
    }

    private sealed class AboutUpdateSource : IAppUpdateSource
    {
        public Task<AppRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
            => Task.FromResult<AppRelease?>(new("v1.0.2", new(1, 0, 2), "Improved recognition and update support.", new("https://example.com"), 117041768, "fixture"));
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

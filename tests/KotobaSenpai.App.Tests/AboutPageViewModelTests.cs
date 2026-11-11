using System.Globalization;
using System.IO;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Updates;

namespace KotobaSenpai.App.Tests;

public sealed class AboutPageViewModelTests
{
    [Fact]
    public async Task First_visit_checks_once_and_requires_separate_download_and_install_actions()
    {
        var source = new Source();
        var installer = new Installer();
        using var page = Page(source, installer);
        Assert.Equal(0, source.Checks);
        Assert.Equal("1.0.1", page.CurrentVersion);
        await page.EnsureCheckedAsync();
        await page.EnsureCheckedAsync();
        Assert.Equal(1, source.Checks);
        Assert.True(page.HasUpdate);
        Assert.Equal(0, source.Downloads);
        Assert.False(page.InstallCommand.CanExecute(null));

        await page.DownloadCommand.ExecuteAsync(null);
        Assert.True(page.IsReady);
        Assert.False(page.DownloadCommand.CanExecute(null));
        Assert.Equal(0, installer.Launches);
        var closeRequested = false;
        page.InstallStarted += (_, _) => closeRequested = true;
        await page.InstallCommand.ExecuteAsync(null);
        Assert.Equal(1, installer.Launches);
        Assert.True(closeRequested);
        Assert.False(page.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Check_errors_remain_retryable_and_retranslate_when_language_changes()
    {
        var source = new Source { CheckFailure = new AppUpdateException(ErrorCodes.AppUpdateRateLimited, "Rate limit test.") };
        var localizer = LocalizerFactory.Create(CultureInfo.GetCultureInfo("en"));
        using var page = new AboutPageViewModel(source, new Installer(), localizer, new UserMessageResolver(localizer),
            ViewModelDependencies.Logger, new(1, 0, 1));
        await page.CheckCommand.ExecuteAsync(null);
        Assert.False(page.HasUpdate);
        Assert.True(page.CheckCommand.CanExecute(null));
        var english = page.Status;
        Assert.Contains("GitHub", english);
        localizer.ApplyCulture(CultureInfo.GetCultureInfo("zh-CN"));
        Assert.NotEqual(english, page.Status);
        Assert.DoesNotContain("Rate limit test.", page.Status);
        source.CheckFailure = null;
        source.Latest = null;
        await page.CheckCommand.ExecuteAsync(null);
        Assert.False(page.HasUpdate);
        Assert.False(page.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cancelling_or_disposing_an_active_download_never_enables_installation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { Download = async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        } };
        using var page = Page(source, new Installer());
        await page.EnsureCheckedAsync();
        var download = page.DownloadCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(page.IsDownloading);
        Assert.False(page.CheckCommand.CanExecute(null));
        Assert.False(page.DownloadCommand.CanExecute(null));
        page.CancelDownloadCommand.Execute(null);
        await download;
        Assert.False(page.IsReady);
        Assert.True(page.DownloadCommand.CanExecute(null));
        Assert.Contains("cancelled", page.Status);
        var retry = page.DownloadCommand.ExecuteAsync(null);
        page.Dispose();
        await retry;
        Assert.False(page.IsReady);
        Assert.False(page.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task Installer_failure_keeps_the_app_open_and_requires_a_fresh_download()
    {
        using var page = Page(new Source(), new Installer { Failure = new IOException("Installer test failure.") });
        var closeRequested = false;
        page.InstallStarted += (_, _) => closeRequested = true;
        await page.EnsureCheckedAsync();
        await page.DownloadCommand.ExecuteAsync(null);
        await page.InstallCommand.ExecuteAsync(null);
        Assert.False(closeRequested);
        Assert.False(page.IsBusy);
        Assert.False(page.IsReady);
        Assert.True(page.DownloadCommand.CanExecute(null));
        Assert.DoesNotContain("Installer test failure.", page.Status);
    }

    [Fact]
    public async Task Development_builds_can_download_but_cannot_auto_install()
    {
        using var page = Page(new Source(), new Installer { IsAvailable = false });
        await page.EnsureCheckedAsync();
        await page.DownloadCommand.ExecuteAsync(null);
        Assert.True(page.IsReady);
        Assert.True(page.RequiresManualInstall);
        Assert.False(page.InstallCommand.CanExecute(null));
    }

    private static AboutPageViewModel Page(Source source, Installer installer)
    {
        var localizer = LocalizerFactory.Create(CultureInfo.GetCultureInfo("en"));
        return new(source, installer, localizer, new UserMessageResolver(localizer), ViewModelDependencies.Logger, new(1, 0, 1, 0));
    }

    private sealed class Source : IAppUpdateSource
    {
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public Exception? CheckFailure { get; set; }
        public AppRelease? Latest { get; set; } = new("v1.0.2", new(1, 0, 2), "Release notes.", new("https://example.com"), 100, "unused");
        public Func<CancellationToken, Task>? Download { get; set; }

        public Task<AppRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
        {
            Checks++;
            return CheckFailure is null ? Task.FromResult(Latest) : Task.FromException<AppRelease?>(CheckFailure);
        }
        public async Task<DownloadedAppUpdate> DownloadAsync(AppRelease release, IProgress<AppUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Downloads++;
            if (Download is not null) await Download(cancellationToken);
            return new(release, "test-installer.exe");
        }
    }

    private sealed class Installer : IAppUpdateInstaller
    {
        public bool IsAvailable { get; set; } = true;
        public int Launches { get; private set; }
        public Exception? Failure { get; set; }
        public Task LaunchAsync(DownloadedAppUpdate update, CancellationToken cancellationToken)
        {
            Launches++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
}

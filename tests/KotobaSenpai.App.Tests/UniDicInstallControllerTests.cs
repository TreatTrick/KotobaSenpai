using KotobaSenpai.Core.Errors;
using System.Reflection;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Localization;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Tests;

public sealed class UniDicInstallControllerTests
{
    private static UniDicInstallController Create(Func<CancellationToken, Task> install)
        => new(new DictionaryInstallerStub(install), new UserMessageResolver(new FakeLocalizer()));

    [Fact]
    public async Task Successful_install_hides_overlay()
    {
        var controller = Create(_ => Task.CompletedTask);

        await controller.InstallCommand.ExecuteAsync(null);

        Assert.True(controller.IsInstalled);
        Assert.False(controller.HasError);
        Assert.False(controller.IsBlocking);
    }

    [Fact]
    public async Task Failed_install_shows_error_and_blocks()
    {
        var controller = Create(_ => throw new InvalidOperationException("boom"));

        await controller.InstallCommand.ExecuteAsync(null);

        Assert.True(controller.HasError);
        Assert.True(controller.IsBlocking);
        Assert.Equal(ErrorCodes.UniDicDownloadFailed, controller.Error);
    }

    [Fact]
    public async Task Retry_after_failure_can_succeed_and_unblock()
    {
        var failOnce = true;
        var controller = Create(_ =>
        {
            if (failOnce)
            {
                failOnce = false;
                throw new InvalidOperationException("boom");
            }
            return Task.CompletedTask;
        });

        await controller.InstallCommand.ExecuteAsync(null);
        Assert.True(controller.HasError);

        await controller.InstallCommand.ExecuteAsync(null);
        Assert.False(controller.HasError);
        Assert.False(controller.IsBlocking);
        Assert.True(controller.IsInstalled);
    }

    [Fact]
    public void Validation_after_known_download_progress_keeps_progress_determinate()
    {
        using var controller = Create(_ => Task.CompletedTask);
        var reportStage = typeof(UniDicInstallController).GetMethod("ReportStage", BindingFlags.Instance | BindingFlags.NonPublic)!;

        reportStage.Invoke(controller, [new DictionaryInstallProgress(DictionaryInstallStage.Downloading, 0.6)]);
        reportStage.Invoke(controller, [new DictionaryInstallProgress(DictionaryInstallStage.Validating)]);

        Assert.Equal(0.6, controller.Progress);
        Assert.False(controller.IsIndeterminate);
    }

    private sealed class FakeLocalizer : IStringLocalizer
    {
#pragma warning disable CS0067 // event required by the interface; tests do not subscribe
        public event EventHandler? CultureChanged;
#pragma warning restore CS0067
        public string Get(string key, params object[] args) => key;
    }
}

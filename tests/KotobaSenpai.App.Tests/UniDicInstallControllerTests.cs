using KotobaSenpai.Core.Errors;
using System.IO;
using System.Net.Http;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.App.Localization;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.App.Tests;

public sealed class UniDicInstallControllerTests
{
    private static UniDicInstallController Create(Func<CancellationToken, IProgress<double>, Task> install)
    {
        var installer = new UniDicDictionaryInstaller(
            new HttpClient(),
            Path.Combine(Path.GetTempPath(), "kotoba-unidic-tests"),
            new UniDicManifest("v", "url", "sha", "fmt", ["char.bin"]));
        var resolver = new UserMessageResolver(new FakeLocalizer());
        return new UniDicInstallController(installer, resolver, install);
    }

    [Fact]
    public async Task Successful_install_hides_overlay()
    {
        var controller = Create((_, _) => Task.CompletedTask);

        await controller.InstallCommand.ExecuteAsync(null);

        Assert.True(controller.IsInstalled);
        Assert.False(controller.HasError);
        Assert.False(controller.IsBlocking);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_dictionary_override_is_validated_without_downloading_a_default(bool valid)
    {
        var root = Path.Combine(Path.GetTempPath(), "kotoba-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (valid)
            {
                foreach (var name in UniDicAssets.RequiredRuntimeFiles) File.WriteAllText(Path.Combine(root, name), "fixture");
                File.WriteAllText(Path.Combine(root, UniDicAssets.DicrcFileName), UniDicAssets.Format);
            }
            using var installer = new UniDicDictionaryInstaller(new HttpClient(new NoNetworkHandler()),
                Path.Combine(root, "default"), new UniDicManifest("v", "url", "sha", "fmt", ["char.bin"]));
            using var controller = new UniDicInstallController(installer, new UserMessageResolver(new FakeLocalizer()),
                dictionaryOverride: () => root);
            Assert.Equal(valid, controller.CheckInstalled());
            await controller.InstallAsync();
            Assert.Equal(valid, controller.IsInstalled);
            Assert.Equal(!valid, controller.HasError);
            if (valid) Assert.Equal(DictionaryInstallStage.Ready, controller.Stage);
            Assert.False(Directory.Exists(Path.Combine(root, "default")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("An explicit dictionary override must not download the default dictionary.");
    }

    [Fact]
    public async Task Failed_install_shows_error_and_blocks()
    {
        var controller = Create((_, _) => throw new InvalidOperationException("boom"));

        await controller.InstallCommand.ExecuteAsync(null);

        Assert.True(controller.HasError);
        Assert.True(controller.IsBlocking);
        Assert.Equal(ErrorCodes.UniDicDownloadFailed, controller.Error);
    }

    [Fact]
    public async Task Retry_after_failure_can_succeed_and_unblock()
    {
        var failOnce = true;
        var controller = Create((_, _) =>
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

    private sealed class FakeLocalizer : IStringLocalizer
    {
#pragma warning disable CS0067 // event required by the interface; tests do not subscribe
        public event EventHandler? CultureChanged;
#pragma warning restore CS0067
        public string Get(string key, params object[] args) => key;
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Updates;
using KotobaSenpai.Platform.Windows.Updates;

namespace KotobaSenpai.Platform.Windows.Tests;

public sealed class AppUpdateTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "KotobaSenpai-update-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("A verified installer test payload.");

    [Theory]
    [InlineData("v1.10.0", "1.9.0", true)]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("v1.0.1", "1.0.1", false)]
    [InlineData("v1.0.0", "1.0.1", false)]
    public async Task Checks_numeric_versions_without_downgrading(string tag, string current, bool available)
    {
        using var source = Source((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/TreatTrick/KotobaSenpai/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Contains("KotobaSenpai/", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(JsonResponse(ReleaseJson(tag)));
        });
        var release = await source.CheckAsync(Version.Parse(current), CancellationToken.None);
        Assert.Equal(available, release is not null);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("draft")]
    [InlineData("tag")]
    [InlineData("double-prefix")]
    [InlineData("leading-zero")]
    [InlineData("name")]
    [InlineData("url")]
    [InlineData("digest")]
    [InlineData("missing")]
    [InlineData("json")]
    public async Task Rejects_unusable_or_untrusted_release_metadata(string defect)
    {
        var json = ReleaseJson("v1.10.0", defect);
        using var source = Source((_, _) => Task.FromResult(JsonResponse(json)));
        var failure = await Assert.ThrowsAsync<AppUpdateException>(() => source.CheckAsync(new(1, 0, 0), CancellationToken.None));
        Assert.Equal(ErrorCodes.AppUpdateReleaseUnavailable, failure.ErrorCode);
    }

    [Theory]
    [InlineData(403, ErrorCodes.AppUpdateRateLimited)]
    [InlineData(429, ErrorCodes.AppUpdateRateLimited)]
    [InlineData(404, ErrorCodes.AppUpdateReleaseUnavailable)]
    public async Task Distinguishes_release_errors_from_an_up_to_date_result(int status, string code)
    {
        using var source = Source((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var failure = await Assert.ThrowsAsync<AppUpdateException>(() => source.CheckAsync(new(1, 0, 0), CancellationToken.None));
        Assert.Equal(code, failure.ErrorCode);
    }

    [Fact]
    public async Task Downloads_then_verifies_before_exposing_an_executable()
    {
        using var source = Source((request, _) =>
        {
            Assert.Equal(Release().DownloadUri, request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
        });
        var stages = new List<AppUpdateProgress>();
        var result = await source.DownloadAsync(Release(), new ImmediateProgress(stages.Add), CancellationToken.None);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(result.InstallerPath));
        Assert.Contains(stages, value => value.IsVerifying);
        Assert.Empty(Directory.GetFiles(_directory, "*.partial", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("long")]
    public async Task Corrupted_downloads_never_become_installers(string defect)
    {
        var bytes = defect == "short" ? Payload[..^1] : defect == "long" ? Payload.Concat(new byte[] { 0 }).ToArray() : new byte[Payload.Length];
        using var source = Source((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var failure = await Assert.ThrowsAsync<AppUpdateException>(() => source.DownloadAsync(Release(), new ImmediateProgress(_ => { }), CancellationToken.None));
        Assert.Equal(ErrorCodes.AppUpdateVerificationFailed, failure.ErrorCode);
        Assert.Empty(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancelling_a_body_read_removes_the_partial_file_and_allows_retry()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var source = Source((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = ++calls == 1 ? new StreamContent(new WaitingStream(entered)) : new ByteArrayContent(Payload),
        }));
        var download = source.DownloadAsync(Release(), new ImmediateProgress(_ => { }), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
        Assert.Empty(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories));
        var retry = await source.DownloadAsync(Release(), new ImmediateProgress(_ => { }), CancellationToken.None);
        Assert.True(File.Exists(retry.InstallerPath));
    }

    [Fact]
    public async Task Installer_revalidates_the_file_and_passes_a_safe_handoff_to_the_registered_directory()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Release().InstallerName);
        await File.WriteAllBytesAsync(path, Payload);
        ProcessStartInfo? launched = null;
        var installDirectory = Path.Combine(_directory, "Installed app with spaces");
        var installer = new InnoSetupUpdateInstaller(installDirectory, () => installDirectory + Path.DirectorySeparatorChar, info => launched = info);
        await installer.LaunchAsync(new(Release(), path), CancellationToken.None);
        Assert.NotNull(launched);
        Assert.False(launched.UseShellExecute);
        Assert.Equal(path, launched.FileName);
        Assert.Contains("/SILENT", launched.ArgumentList);
        Assert.Contains("/NORESTART", launched.ArgumentList);
        Assert.Contains("/APPUPDATE=1", launched.ArgumentList);
        Assert.Contains($"/UPDATEPID={Environment.ProcessId}", launched.ArgumentList);
        Assert.Contains("/DIR=" + installDirectory, launched.ArgumentList);
        Assert.DoesNotContain("/FORCECLOSEAPPLICATIONS", launched.ArgumentList);

        launched = null;
        await File.WriteAllBytesAsync(path, new byte[Payload.Length]);
        var failure = await Assert.ThrowsAsync<AppUpdateException>(() => installer.LaunchAsync(new(Release(), path), CancellationToken.None));
        Assert.Equal(ErrorCodes.AppUpdateVerificationFailed, failure.ErrorCode);
        Assert.Null(launched);
    }

    [Fact]
    public async Task Development_directories_cannot_launch_installers()
    {
        var launched = false;
        var installer = new InnoSetupUpdateInstaller(_directory, () => _directory + "-installed", _ => launched = true);
        Assert.False(installer.IsAvailable);
        var failure = await Assert.ThrowsAsync<AppUpdateException>(() => installer.LaunchAsync(new(Release(), "unused"), CancellationToken.None));
        Assert.Equal(ErrorCodes.AppUpdateInstallUnavailable, failure.ErrorCode);
        Assert.False(launched);
    }

    [Fact]
    public async Task A_cancelled_handoff_or_missing_file_never_starts_the_installer()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Release().InstallerName);
        await File.WriteAllBytesAsync(path, Payload);
        var launched = false;
        var installer = new InnoSetupUpdateInstaller(_directory, () => _directory, _ => launched = true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.LaunchAsync(new(Release(), path), cancellation.Token));
        Assert.False(launched);
        File.Delete(path);
        await Assert.ThrowsAsync<FileNotFoundException>(() => installer.LaunchAsync(new(Release(), path), CancellationToken.None));
        Assert.False(launched);
    }

    private GitHubAppUpdateSource Source(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new HttpClient(new CallbackHandler(send)), _directory);

    private static AppRelease Release() => new("v1.10.0", new(1, 10, 0), "Release notes.",
        new("https://github.com/TreatTrick/KotobaSenpai/releases/download/v1.10.0/KotobaSenpai-1.10.0-win-x64-Setup.exe"),
        Payload.Length, Convert.ToHexString(SHA256.HashData(Payload)));

    private static string ReleaseJson(string tag, string? defect = null)
    {
        var version = tag.TrimStart('v');
        var name = $"KotobaSenpai-{version}-win-x64-Setup.exe";
        return defect == "json" ? "invalid JSON" : JsonSerializer.Serialize(new
        {
            tag_name = defect == "tag" ? "v1.10.0-preview" : defect == "double-prefix" ? "vv1.10.0"
                : defect == "leading-zero" ? "v01.10.0" : tag,
            draft = defect == "draft", prerelease = defect == "preview", body = "Release notes.",
            assets = defect == "missing" ? [] : new[] { new
            {
                name = defect == "name" ? "source.zip" : name, state = "uploaded", size = Payload.Length,
                digest = defect == "digest" ? null : "sha256:" + Convert.ToHexString(SHA256.HashData(Payload)),
                browser_download_url = defect == "url" ? "https://example.com/installer.exe"
                    : $"https://github.com/TreatTrick/KotobaSenpai/releases/download/{tag}/{name}",
            } },
        });
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class ImmediateProgress(Action<AppUpdateProgress> report) : IProgress<AppUpdateProgress>
    {
        public void Report(AppUpdateProgress value) => report(value);
    }
    private sealed class WaitingStream(TaskCompletionSource entered) : MemoryStream
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

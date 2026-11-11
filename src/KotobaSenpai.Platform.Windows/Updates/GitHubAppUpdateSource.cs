using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Updates;

namespace KotobaSenpai.Platform.Windows.Updates;

/// <summary>Downloads only versioned Windows installers from the project's public GitHub releases.</summary>
public sealed class GitHubAppUpdateSource(HttpClient http, string downloadDirectory) : IAppUpdateSource, IDisposable
{
    public async Task<AppRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/TreatTrick/KotobaSenpai/releases/latest");
            request.Headers.UserAgent.ParseAdd("KotobaSenpai/" + currentVersion.ToString(3));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new AppUpdateException(ErrorCodes.AppUpdateRateLimited, "GitHub rejected the release request due to a rate limit or access restriction.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new AppUpdateException(ErrorCodes.AppUpdateReleaseUnavailable, "No published release is available.");
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<ReleaseResponse>(timeout.Token).ConfigureAwait(false);
            if (data is null || data.Draft || data.Prerelease || !UpdatePackage.TryParseVersion(data.Tag, out var version))
                throw new AppUpdateException(ErrorCodes.AppUpdateReleaseUnavailable, "GitHub did not return a supported stable release.");
            if (version <= new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build)))
                return null;

            var name = $"KotobaSenpai-{version.ToString(3)}-win-x64-Setup.exe";
            var asset = data.Assets?.FirstOrDefault(item => item.Name == name && item.State == "uploaded");
            if (asset is null || !Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri)
                || asset.Digest?.StartsWith("sha256:", StringComparison.Ordinal) != true)
                throw new AppUpdateException(ErrorCodes.AppUpdateReleaseUnavailable, "The release is missing its installer or SHA-256 digest.");
            var release = new AppRelease(data.Tag!, version, data.Body ?? string.Empty, uri, asset.Size, asset.Digest[7..]);
            UpdatePackage.ValidateRelease(release);
            return release;
        }
        catch (JsonException exception)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateReleaseUnavailable, "The release response is invalid.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateTimedOut, "The release check timed out.", exception);
        }
    }

    public async Task<DownloadedAppUpdate> DownloadAsync(AppRelease release, IProgress<AppUpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        UpdatePackage.ValidateRelease(release);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var directory = Path.Combine(downloadDirectory, release.Version.ToString(3));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, release.InstallerName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await http.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new AppUpdateException(ErrorCodes.AppUpdateVerificationFailed, "The download content length does not match the release.");

            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                var clock = Stopwatch.StartNew();
                progress.Report(new(0, release.Size));
                while (true)
                {
                    // A slow download may run for minutes; only a stalled read has a deadline.
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (count == 0) break;
                    received += count;
                    if (received > release.Size)
                        throw new AppUpdateException(ErrorCodes.AppUpdateVerificationFailed, "The download exceeds the declared release size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    if (clock.ElapsedMilliseconds >= 100)
                    {
                        progress.Report(new(received, release.Size));
                        clock.Restart();
                    }
                }
            }

            progress.Report(new(release.Size, release.Size, IsVerifying: true));
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await using (await UpdatePackage.OpenVerifiedAsync(new(release, temporary), timeout.Token).ConfigureAwait(false)) { }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return new(release, destination);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppUpdateException(ErrorCodes.AppUpdateTimedOut, "The update download or verification timed out.", exception);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Dispose() => http.Dispose();

    private sealed record ReleaseResponse(
        [property: JsonPropertyName("tag_name")] string? Tag,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] AssetResponse[]? Assets);

    private sealed record AssetResponse(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("browser_download_url")] string? DownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);
}

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Platform.Windows;

namespace KotobaSenpai.App.Japanese;

/// <summary>
/// UniDic dictionary installer (M1 distribution): on first run downloads from a fixed URL and atomically installs into the local cache directory,
/// and also supports importing from a validated offline archive. After download/extraction it validates the SHA-256, version, <c>unidic22</c> format, and
/// the four runtime files, and only after all pass does it atomically replace <c>dicdir</c> and write an installed-manifest.
/// Installation is cancellable, retryable, and never leaves a half-finished result; a named Mutex guarantees that only one process replaces the dictionary at a time.
/// Failures throw <see cref="WindowsPlatformException"/>: hash/format mismatches throw <c>UniDicDictionaryInvalid</c>,
/// network/extraction I/O failures throw <c>UniDicDownloadFailed</c>.
/// </summary>
public sealed class UniDicDictionaryInstaller : IDisposable
{
    private const string MutexName = @"Local\KotobaSenpai.UniDic.Install";

    private readonly string _cacheRoot;
    private readonly string _dicDir;
    private readonly HttpClient _http;
    private readonly UniDicManifest _manifest;
    private readonly SemaphoreSlim _inProcessLock = new(1, 1);
    private bool _disposed;

    /// <summary>The final dictionary directory (<c>&lt;cacheRoot&gt;/dicdir</c>).</summary>
    public string DictionaryDirectory => _dicDir;

    /// <summary>Uses the real cache root directory and the fixed asset manifest.</summary>
    public UniDicDictionaryInstaller(HttpClient http)
        : this(http, DefaultCacheRoot(), DefaultManifest())
    {
    }

    /// <summary>
    /// <paramref name="cacheRoot"/> and <paramref name="manifest"/> can be injected (network-free unit tests use a temp directory and fixture hashes).
    /// </summary>
    public UniDicDictionaryInstaller(HttpClient http, string cacheRoot, UniDicManifest manifest)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
        _manifest = manifest;
        _dicDir = Path.Combine(_cacheRoot, "dicdir");
    }

    /// <summary>True: the four runtime files, expected version/format, and the installed manifest are all valid.</summary>
    public bool IsInstalled
    {
        get
        {
            if (UniDicAssets.RequiredRuntimeFiles.Any(f => !File.Exists(Path.Combine(_dicDir, f))))
                return false;
            if (!VersionAndFormatValid(_dicDir))
                return false;
            return ManifestFileMatches(_dicDir);
        }
    }

    /// <summary>Returns immediately when already installed; otherwise downloads, validates, and atomically installs. Safe to trigger in parallel before installation.</summary>
    public async Task EnsureInstalledAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled)
            return;

        await _inProcessLock.WaitAsync(ct);
        try
        {
            if (IsInstalled)
                return;

            var stagingRoot = Path.Combine(_cacheRoot, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            var zipPath = Path.Combine(stagingRoot, "unidic-3.1.0.zip");
            try
            {
                await DownloadAsync(UniDicAssets.SourceUrl, zipPath, progress, ct);
                ct.ThrowIfCancellationRequested();
                await InstallFromArchiveCoreAsync(zipPath, stagingRoot, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WindowsPlatformException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new WindowsPlatformException(
                    ErrorCodes.UniDicDownloadFailed,
                    $"UniDic download/extraction failed from '{UniDicAssets.SourceUrl}'.",
                    ex);
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
        finally
        {
            _inProcessLock.Release();
        }
    }

    /// <summary>Imports from a local archive, reusing the same hash/version/format/file-set and atomic-replacement validation, without depending on the network.</summary>
    public async Task InstallFromArchiveAsync(string archivePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found.", archivePath);

        await _inProcessLock.WaitAsync(ct);
        try
        {
            var stagingRoot = Path.Combine(_cacheRoot, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            try
            {
                await InstallFromArchiveCoreAsync(archivePath, stagingRoot, ct);
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }
        }
        finally
        {
            _inProcessLock.Release();
        }
    }

    /// <summary>The install core shared by online and offline paths: SHA-256 → extract → locate → version/format validation → write manifest → atomic replace.</summary>
    private async Task InstallFromArchiveCoreAsync(string archivePath, string stagingRoot, CancellationToken ct)
    {
        var sha = await ComputeSha256Async(archivePath, ct);
        if (!string.Equals(sha, _manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                $"UniDic archive SHA-256 mismatch (got {sha}, expected {_manifest.Sha256}).");

        ZipFile.ExtractToDirectory(archivePath, stagingRoot);
        ct.ThrowIfCancellationRequested();

        var dicDir = LocateDicDir(stagingRoot)
            ?? throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                "UniDic archive missing required runtime files.");
        if (!VersionAndFormatValid(dicDir))
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                "UniDic archive version/format does not match expected unidic22 asset.");

        // Assemble the final content (including the manifest), then take the cross-process lock and atomically replace.
        var finalStaging = Path.Combine(_cacheRoot, "final-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(finalStaging);
        MoveContents(dicDir, finalStaging);
        WriteManifest(finalStaging);

        await PromoteAsync(finalStaging, ct);
    }

    /// <summary>Serializes the dicdir replacement across processes: back up the old directory → atomically rename in the new one → clean up the backup.</summary>
    private async Task PromoteAsync(string finalStaging, CancellationToken ct)
    {
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                throw new WindowsPlatformException(ErrorCodes.UniDicDownloadFailed, "Timed out waiting for dictionary install lock.");
        }
        catch (AbandonedMutexException)
        {
            // A lock left behind by a crashed prior process: the Mutex has been granted, so we may continue.
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            // Another process may have already installed it; if so, just clean up this run's staging.
            if (IsInstalled)
            {
                TryDeleteDirectory(finalStaging);
                return;
            }

            var backup = _dicDir + ".old";
            TryDeleteDirectory(backup);
            if (Directory.Exists(_dicDir))
                Directory.Move(_dicDir, backup);

            try
            {
                Directory.Move(finalStaging, _dicDir);
            }
            catch
            {
                // Rollback: restore the old directory.
                if (Directory.Exists(backup) && !Directory.Exists(_dicDir))
                    Directory.Move(backup, _dicDir);
                throw;
            }

            TryDeleteDirectory(backup);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private async Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        var buffer = new byte[1 << 16];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            received += read;
            if (total.HasValue && progress is not null)
                progress.Report(total.Value == 0 ? 0 : (double)received / total.Value);
        }
        progress?.Report(1.0);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Locates the directory containing the four runtime files under the extraction root (top level preferred; the zip's top-level name varies).</summary>
    private static string? LocateDicDir(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
            if (ContainsAllRuntimeFiles(dir))
                return dir;
        if (ContainsAllRuntimeFiles(root))
            return root;
        return null;
    }

    private static bool ContainsAllRuntimeFiles(string dir)
        => UniDicAssets.RequiredRuntimeFiles.All(f => File.Exists(Path.Combine(dir, f)));

    private static bool VersionAndFormatValid(string dicDir)
    {
        // Empirically, cotonoha-dic's unidic-3.1.0.zip contains no version file (only dicrc/README/licenses);
        // version and identity are already pinned by the fixed URL + SHA-256 validation, so here we only validate the unidic22 format marker carried by dicrc.
        return File.Exists(Path.Combine(dicDir, UniDicAssets.DicrcFileName))
            && File.ReadAllText(Path.Combine(dicDir, UniDicAssets.DicrcFileName))
                .Contains(UniDicAssets.Format, StringComparison.Ordinal);
    }

    private void WriteManifest(string dicDir)
    {
        var json = JsonSerializer.Serialize(_manifest);
        File.WriteAllText(Path.Combine(dicDir, UniDicAssets.ManifestFileName), json);
    }

    private bool ManifestFileMatches(string dicDir)
    {
        var path = Path.Combine(dicDir, UniDicAssets.ManifestFileName);
        if (!File.Exists(path))
            return false;
        try
        {
            var m = JsonSerializer.Deserialize<UniDicManifest>(File.ReadAllText(path));
            return m is not null
                && m.Version == _manifest.Version
                && m.Sha256 == _manifest.Sha256
                && m.Format == _manifest.Format;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Moves source's top-level files and subdirectories into destination as a whole (preserving subdirectories, e.g. licenses/).</summary>
    private static void MoveContents(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source))
            File.Move(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            Directory.Move(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string DefaultCacheRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KotobaSenpai", "UniDic");

    private static UniDicManifest DefaultManifest() => new(
        UniDicAssets.Version,
        UniDicAssets.SourceUrl,
        UniDicAssets.Sha256,
        UniDicAssets.Format,
        UniDicAssets.RequiredRuntimeFiles);

    public void Dispose()
    {
        if (_disposed)
            return;
        _inProcessLock.Dispose();
        _disposed = true;
    }
}
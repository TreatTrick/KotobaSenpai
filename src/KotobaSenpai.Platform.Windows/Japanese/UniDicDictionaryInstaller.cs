using KotobaSenpai.Core.Errors;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.Platform.Windows.Japanese;

/// <summary>
/// UniDic dictionary installer: on first run installs the bundled archive into the local cache without network access.
/// It also supports importing another copy of the pinned archive. It validates the SHA-256, version, <c>unidic22</c> format, and
/// the four runtime files, and only after all pass does it atomically replace <c>dicdir</c> and write an installed-manifest.
/// Installation is cancellable, retryable, and never leaves a half-finished result; a named Mutex guarantees that only one process replaces the dictionary at a time.
/// Failures throw <see cref="WindowsPlatformException"/>: hash/format mismatches throw <c>UniDicDictionaryInvalid</c>,
/// extraction/installation I/O failures throw <c>UniDicInstallFailed</c>.
/// </summary>
public sealed class UniDicDictionaryInstaller : IUniDicDictionaryInstaller
{
    private const string MutexName = @"Local\KotobaSenpai.UniDic.Install";

    private readonly string _cacheRoot;
    private readonly string _dicDir;
    private readonly string _archivePath;
    private readonly UniDicManifest _manifest;
    private readonly Func<string?> _dictionaryOverride;
    private readonly SemaphoreSlim _inProcessLock = new(1, 1);

    /// <summary>The final dictionary directory (<c>&lt;cacheRoot&gt;/dicdir</c>).</summary>
    public string DictionaryDirectory => _dicDir;

    /// <summary>Uses the real cache root directory and the fixed asset manifest.</summary>
    public UniDicDictionaryInstaller()
        : this(Path.Combine(AppContext.BaseDirectory, "Assets", "UniDic", UniDicAssets.ArchiveFileName),
            DefaultCacheRoot(), DefaultManifest(),
            () => Environment.GetEnvironmentVariable("KOTOBA_UNIDIC_DIR"))
    {
    }

    /// <summary>
    /// The archive path, cache root, and manifest can be injected for local assets and fixture-based tests.
    /// </summary>
    public UniDicDictionaryInstaller(string archivePath, string cacheRoot, UniDicManifest manifest,
        Func<string?>? dictionaryOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        _archivePath = archivePath;
        _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
        _manifest = manifest;
        _dictionaryOverride = dictionaryOverride ?? (() => null);
        _dicDir = Path.Combine(_cacheRoot, "dicdir");
    }

    /// <summary>Checks the explicit runtime override, or the validated default installation when no override is set.</summary>
    public bool IsInstalled
    {
        get
        {
            var directory = _dictionaryOverride();
            if (string.IsNullOrWhiteSpace(directory)) return IsCacheInstalled;
            try
            {
                UniDicTokenizer.ValidateDictionary(directory, requireInstalledManifest: false);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    private bool IsCacheInstalled
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

    /// <summary>Returns immediately when installed; otherwise validates and atomically installs the bundled archive. Safe to trigger in parallel before installation.</summary>
    public async Task EnsureInstalledAsync(CancellationToken ct = default,
        IProgress<DictionaryInstallProgress>? stages = null)
    {
        if (IsInstalled)
            return;

        var directory = _dictionaryOverride();
        if (!string.IsNullOrWhiteSpace(directory))
        {
            stages?.Report(new(DictionaryInstallStage.Validating));
            await Task.Run(() => UniDicTokenizer.ValidateDictionary(directory, requireInstalledManifest: false), ct).ConfigureAwait(false);
            stages?.Report(new(DictionaryInstallStage.Ready));
            return;
        }

        await _inProcessLock.WaitAsync(ct);
        try
        {
            if (IsCacheInstalled)
                return;

            if (!File.Exists(_archivePath))
                throw new WindowsPlatformException(ErrorCodes.UniDicDictionaryMissing,
                    $"Bundled UniDic archive not found at '{_archivePath}'. Reinstall the complete application package.");

            var stagingRoot = Path.Combine(_cacheRoot, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);
            try
            {
                ct.ThrowIfCancellationRequested();
                await InstallFromArchiveCoreAsync(_archivePath, stagingRoot, ct, stages).ConfigureAwait(false);
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
                    ErrorCodes.UniDicInstallFailed,
                    $"UniDic installation failed from bundled archive '{_archivePath}'.",
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
    public async Task InstallFromArchiveAsync(string archivePath, CancellationToken ct = default,
        IProgress<DictionaryInstallProgress>? stages = null)
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
                await InstallFromArchiveCoreAsync(archivePath, stagingRoot, ct, stages).ConfigureAwait(false);
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

    /// <summary>The install core: SHA-256 → extract → locate → version/format validation → write manifest → atomic replace.</summary>
    private async Task InstallFromArchiveCoreAsync(string archivePath, string stagingRoot, CancellationToken ct,
        IProgress<DictionaryInstallProgress>? stages)
    {
        stages?.Report(new(DictionaryInstallStage.Validating));
        var sha = await ComputeSha256Async(archivePath, ct).ConfigureAwait(false);
        if (!string.Equals(sha, _manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                $"UniDic archive SHA-256 mismatch (got {sha}, expected {_manifest.Sha256}).");

        stages?.Report(new(DictionaryInstallStage.Extracting));
        await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, stagingRoot), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var dicDir = LocateDicDir(stagingRoot)
            ?? throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                "UniDic archive missing required runtime files.");
        if (!VersionAndFormatValid(dicDir))
            throw new WindowsPlatformException(
                ErrorCodes.UniDicDictionaryInvalid,
                "UniDic archive version/format does not match expected unidic22 asset.");

        stages?.Report(new(DictionaryInstallStage.Installing));
        // Assemble the final content (including the manifest), then take the cross-process lock and atomically replace.
        var finalStaging = Path.Combine(_cacheRoot, "final-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(finalStaging);
        MoveContents(dicDir, finalStaging);
        WriteManifest(finalStaging);

        Promote(finalStaging, ct);
        stages?.Report(new(DictionaryInstallStage.Ready));
    }

    /// <summary>Serializes the dicdir replacement across processes: back up the old directory → atomically rename in the new one → clean up the backup.</summary>
    private void Promote(string finalStaging, CancellationToken ct)
    {
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                throw new WindowsPlatformException(ErrorCodes.UniDicInstallFailed, "Timed out waiting for dictionary install lock.");
        }
        catch (AbandonedMutexException)
        {
            // A lock left behind by a crashed prior process: the Mutex has been granted, so we may continue.
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            // Another process may have already installed it; if so, just clean up this run's staging.
            if (IsCacheInstalled)
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
        // Version and identity are pinned by SHA-256 validation, so here we only validate the unidic22 format marker carried by dicrc.
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
}

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
/// UniDic 词典安装器（M1 分发）：首次运行从固定 URL 下载并原子安装到本地缓存目录，
/// 也支持从已校验的离线压缩包导入。下载/解压后校验 SHA-256、版本、<c>unidic22</c> 格式与
/// 四个运行时文件，全部通过后才原子替换 <c>dicdir</c>，并写入已安装 manifest。
/// 安装过程可取消、可重试且不会留下半成品；跨进程用命名 Mutex 保证同一时刻只有一个进程替换词典。
/// 失败抛 <see cref="WindowsPlatformException"/>：哈希/格式不匹配抛 <c>UniDicDictionaryInvalid</c>，
/// 网络/解压类 I/O 失败抛 <c>UniDicDownloadFailed</c>。
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

    /// <summary>最终词典目录（<c>&lt;cacheRoot&gt;/dicdir</c>）。</summary>
    public string DictionaryDirectory => _dicDir;

    /// <summary>使用真实缓存根目录与固定资产清单。</summary>
    public UniDicDictionaryInstaller(HttpClient http)
        : this(http, DefaultCacheRoot(), DefaultManifest())
    {
    }

    /// <summary>
    /// <paramref name="cacheRoot"/> 与 <paramref name="manifest"/> 可注入（无网络单元测试用临时目录与 fixture 哈希）。
    /// </summary>
    public UniDicDictionaryInstaller(HttpClient http, string cacheRoot, UniDicManifest manifest)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));
        _manifest = manifest;
        _dicDir = Path.Combine(_cacheRoot, "dicdir");
    }

    /// <summary>真：四个运行时文件、期望版本/格式与已安装 manifest 全部有效。</summary>
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

    /// <summary>已安装则立即返回；否则下载→校验→原子安装。可在无安装前并行触发。</summary>
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

    /// <summary>从本地压缩包导入，复用同一哈希/版本/格式/文件集与原子替换验证，不依赖网络。</summary>
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

    /// <summary>在线与离线共用的安装核心：SHA-256 → 解压 → 定位 → 版本/格式校验 → 写 manifest → 原子替换。</summary>
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

        // 组装最终内容（含 manifest），再拿到跨进程锁后原子替换。
        var finalStaging = Path.Combine(_cacheRoot, "final-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(finalStaging);
        MoveContents(dicDir, finalStaging);
        WriteManifest(finalStaging);

        await PromoteAsync(finalStaging, ct);
    }

    /// <summary>跨进程串行化 dicdir 替换：备份旧目录 → 新目录原子改名 → 清理备份。</summary>
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
            // 前任进程崩溃遗留锁：Mutex 已授予，可继续。
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            // 另一进程可能已装好；装好则清理本次 staging 即可。
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
                // 回滚：把旧目录恢复。
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

    /// <summary>在解压根下定位含四个运行时文件的目录（顶层优先，zip 顶层名不定）。</summary>
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
        var versionOk = File.Exists(Path.Combine(dicDir, UniDicAssets.VersionFileName))
            && File.ReadAllText(Path.Combine(dicDir, UniDicAssets.VersionFileName))
                .Contains(UniDicAssets.Version, StringComparison.Ordinal);
        var dicrcOk = File.Exists(Path.Combine(dicDir, UniDicAssets.DicrcFileName))
            && File.ReadAllText(Path.Combine(dicDir, UniDicAssets.DicrcFileName))
                .Contains(UniDicAssets.Format, StringComparison.Ordinal);
        return versionOk && dicrcOk;
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

    /// <summary>把 source 的顶层文件与子目录整体移入 destination（保留子目录，如 licenses/）。</summary>
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
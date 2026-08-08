using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using KotobaSenpai.App.Japanese;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Platform.Windows;

namespace KotobaSenpai.App.Tests;

/// <summary>
/// <see cref="UniDicDictionaryInstaller"/> 测试：用小型 zip fixture 覆盖离线安装、SHA-256 校验、
/// 顶层目录探测、已安装短路、哈希/文件集失败、取消与清理，不联网、不下载 502MB 真实词典。
/// </summary>
public sealed class UniDicDictionaryInstallerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Offline_archive_installs_atomically_and_writes_manifest()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixtureSha.Sha);

        await installer.InstallFromArchiveAsync(fixtureSha.ZipPath);

        Assert.True(installer.IsInstalled);
        foreach (var f in UniDicAssets.RequiredRuntimeFiles)
            Assert.True(File.Exists(Path.Combine(installer.DictionaryDirectory, f)));
        Assert.True(File.Exists(Path.Combine(installer.DictionaryDirectory, UniDicAssets.ManifestFileName)));

        // 无 staging/final 半成品残留。
        var leftovers = Directory.EnumerateDirectories(cacheRoot)
            .Select(Path.GetFileName)
            .Where(n => n != "dicdir");
        Assert.Empty(leftovers);
    }

    [Fact]
    public async Task Already_installed_EnsureInstalledAsync_does_not_touch_network()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixtureSha.Sha);
        await installer.InstallFromArchiveAsync(fixtureSha.ZipPath);

        // 已安装后再次触发：短路返回，绝不发起网络请求。
        var noNetwork = new UniDicDictionaryInstaller(new HttpClient(ThrowingHandler.Instance), cacheRoot, Manif(cacheRoot, fixtureSha.Sha));
        await noNetwork.EnsureInstalledAsync();
        Assert.True(noNetwork.IsInstalled);
    }

    [Fact]
    public async Task Hash_mismatch_throws_unicdic_dictionary_invalid_and_does_not_install()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: true);
        var wrongManifest = Manif(cacheRoot, new string('0', 64));
        var installer = CreateInstaller(cacheRoot, wrongManifest.Sha256);

        var ex = await Assert.ThrowsAsync<WindowsPlatformException>(
            () => installer.InstallFromArchiveAsync(fixtureSha.ZipPath));

        Assert.Equal(ErrorCodes.UniDicDictionaryInvalid, ex.ErrorCode);
        Assert.False(installer.IsInstalled);
        Assert.False(Directory.Exists(installer.DictionaryDirectory));
    }

    [Fact]
    public async Task Archive_missing_runtime_files_throws_unicdic_dictionary_invalid()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: false); // 缺 char.bin 等
        var installer = CreateInstaller(cacheRoot, fixtureSha.Sha);

        var ex = await Assert.ThrowsAsync<WindowsPlatformException>(
            () => installer.InstallFromArchiveAsync(fixtureSha.ZipPath));

        Assert.Equal(ErrorCodes.UniDicDictionaryInvalid, ex.ErrorCode);
        Assert.False(installer.IsInstalled);
    }

    [Fact]
    public async Task Cancelled_install_propagates_and_leaves_no_dicdir()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixtureSha.Sha);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.InstallFromArchiveAsync(fixtureSha.ZipPath, cts.Token));

        Assert.False(Directory.Exists(installer.DictionaryDirectory));
        Assert.Empty(Directory.EnumerateDirectories(cacheRoot)); // 无残留
    }

    [Fact]
    public async Task Concurrent_installs_never_expose_partial_dicdir()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixtureSha.Sha);

        // 同一 installer 并发触发两次安装：进程内锁串行化，均成功且无半成品。
        var tasks = new[]
        {
            installer.InstallFromArchiveAsync(fixtureSha.ZipPath),
            installer.InstallFromArchiveAsync(fixtureSha.ZipPath),
        };
        await Task.WhenAll(tasks);

        Assert.True(installer.IsInstalled);
        var leftovers = Directory.EnumerateDirectories(cacheRoot)
            .Select(Path.GetFileName)
            .Where(n => n != "dicdir");
        Assert.Empty(leftovers);
    }

    private UniDicDictionaryInstaller CreateInstaller(string cacheRoot, string sha)
        => new(new HttpClient(), cacheRoot, Manif(cacheRoot, sha));

    private static UniDicManifest Manif(string cacheRoot, string sha)
        => new(UniDicAssets.Version, "https://example.test/unidic.zip", sha, UniDicAssets.Format, UniDicAssets.RequiredRuntimeFiles);

    /// <summary>构造含 dicdir/（四运行时文件 + version + dicrc）的 zip fixture，返回其路径与 SHA-256。</summary>
    private (string CacheRoot, (string ZipPath, string Sha) Fixture) CreateFixture(bool populate)
    {
        var cacheRoot = NewDir();
        var buildDir = NewDir();
        var dicDir = Path.Combine(buildDir, "dicdir");
        Directory.CreateDirectory(dicDir);

        if (populate)
        {
            foreach (var f in UniDicAssets.RequiredRuntimeFiles)
                File.WriteAllText(Path.Combine(dicDir, f), $"{f}-data");
            File.WriteAllText(Path.Combine(dicDir, UniDicAssets.VersionFileName), $"unidic-{UniDicAssets.Version}");
            File.WriteAllText(Path.Combine(dicDir, UniDicAssets.DicrcFileName), $"output-format-type = {UniDicAssets.Format}");
        }

        // zip 目标必须在被压缩目录之外（否则自包含导致文件锁）。
        var zipPath = Path.Combine(Path.GetTempPath(), "ksfix_" + Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(buildDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        return (cacheRoot, (zipPath, ComputeSha256(zipPath)));
    }

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ksins_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public static readonly ThrowingHandler Instance = new();
        private ThrowingHandler() { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Network must not be contacted while dictionary is already installed.");
    }
}
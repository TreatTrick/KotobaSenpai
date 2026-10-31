using KotobaSenpai.Core.Tokenization;
using KotobaSenpai.Core.Errors;
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
/// <see cref="UniDicDictionaryInstaller"/> tests: a small zip fixture covers offline install, SHA-256 verification,
/// top-level directory detection, already-installed short-circuit, hash/file-set failures, cancellation and cleanup —
/// no network, and no downloading of the real 502MB dictionary.
/// </summary>
public sealed class UniDicDictionaryInstallerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task Offline_install_reports_ready_only_after_validation_extraction_and_promotion()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        using var installer = CreateInstaller(cacheRoot, fixture.Sha);
        var stages = new List<DictionaryInstallStage>();
        var progress = new InlineProgress<DictionaryInstallProgress>(value =>
        {
            stages.Add(value.Stage);
            if (value.Stage == DictionaryInstallStage.Ready) Assert.True(installer.IsInstalled);
        });

        await installer.InstallFromArchiveAsync(fixture.ZipPath, stages: progress);

        Assert.Equal(new[] { DictionaryInstallStage.Validating, DictionaryInstallStage.Extracting,
            DictionaryInstallStage.Installing, DictionaryInstallStage.Ready }, stages);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Download_progress_distinguishes_known_lengths_from_indeterminate_downloads(bool knownLength)
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        using var http = new HttpClient(new ArchiveHandler(File.ReadAllBytes(fixture.ZipPath), knownLength));
        using var installer = new UniDicDictionaryInstaller(http, cacheRoot, Manif(cacheRoot, fixture.Sha));
        var progress = new List<DictionaryInstallProgress>();
        await installer.EnsureInstalledAsync(stages: new InlineProgress<DictionaryInstallProgress>(progress.Add));
        Assert.Contains(progress, item => item.Stage == DictionaryInstallStage.Downloading
            && (knownLength ? item.Fraction == 1 : item.Fraction is null));
        Assert.Equal(DictionaryInstallStage.Ready, progress[^1].Stage);
        Assert.True(installer.IsInstalled);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class ArchiveHandler(byte[] bytes, bool knownLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = knownLength ? new ByteArrayContent(bytes) : new UnknownLengthContent(bytes),
            });
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();
    }

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

        // No leftover staging/final half-products.
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

        // Triggered again after install: short-circuits and never makes a network request.
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
        var (cacheRoot, fixtureSha) = CreateFixture(populate: false); // missing char.bin etc.
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
        Assert.Empty(Directory.EnumerateDirectories(cacheRoot)); // no leftovers
    }

    [Fact]
    public async Task Concurrent_installs_never_expose_partial_dicdir()
    {
        var (cacheRoot, fixtureSha) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixtureSha.Sha);

        // Same installer triggered twice concurrently: the in-process lock serializes them, both succeed with no half-products.
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

    /// <summary>Builds a zip fixture containing dicdir/ (four runtime files + version + dicrc); returns its path and SHA-256.</summary>
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
            File.WriteAllText(Path.Combine(dicDir, UniDicAssets.DicrcFileName), $"output-format-type = {UniDicAssets.Format}");
        }

        // The zip target must be outside the directory being compressed (otherwise self-containment causes a file lock).
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

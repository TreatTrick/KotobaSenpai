using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Japanese;
using KotobaSenpai.Platform.Windows.Japanese;

namespace KotobaSenpai.Platform.Windows.Tests;

/// <summary>Exercises bundled and imported archives, integrity checks, cancellation, and atomic cache installation.</summary>
public sealed class UniDicDictionaryInstallerTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    [Fact]
    public async Task First_run_installs_the_bundled_archive_without_network_access()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixture);
        var stages = new List<DictionaryInstallProgress>();

        await installer.EnsureInstalledAsync(stages: new InlineProgress<DictionaryInstallProgress>(stages.Add));

        Assert.True(installer.IsInstalled);
        Assert.Equal(new[] { DictionaryInstallStage.Validating, DictionaryInstallStage.Extracting,
            DictionaryInstallStage.Installing, DictionaryInstallStage.Ready }, stages.Select(item => item.Stage));
        Assert.True(File.Exists(fixture.ZipPath));
        Assert.True(File.Exists(Path.Combine(installer.DictionaryDirectory, "licenses", "BSD")));
        Assert.Equal(installer.DictionaryDirectory, Assert.Single(Directory.EnumerateDirectories(cacheRoot)));
    }

    [Fact]
    public async Task Missing_bundled_archive_reports_a_missing_dictionary()
    {
        var cacheRoot = NewDir();
        var installer = new UniDicDictionaryInstaller(Path.Combine(cacheRoot, "missing.zip"), cacheRoot, Manifest("test"));

        var exception = await Assert.ThrowsAsync<WindowsPlatformException>(() => installer.EnsureInstalledAsync());

        Assert.Equal(ErrorCodes.UniDicDictionaryMissing, exception.ErrorCode);
        Assert.False(installer.IsInstalled);
        Assert.Empty(Directory.EnumerateFileSystemEntries(cacheRoot));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_dictionary_override_is_validated_without_a_bundled_archive(bool valid)
    {
        var root = NewDir();
        if (valid)
        {
            foreach (var name in UniDicAssets.RequiredRuntimeFiles) File.WriteAllText(Path.Combine(root, name), "fixture");
            File.WriteAllText(Path.Combine(root, UniDicAssets.DicrcFileName), UniDicAssets.Format);
        }
        var cacheRoot = Path.Combine(root, "default");
        IUniDicDictionaryInstaller installer = new UniDicDictionaryInstaller(Path.Combine(root, "missing.zip"),
            cacheRoot, Manifest("test"), dictionaryOverride: () => root);

        Assert.Equal(valid, installer.IsInstalled);
        if (valid)
            await installer.EnsureInstalledAsync();
        else
        {
            var exception = await Assert.ThrowsAsync<WindowsPlatformException>(() => installer.EnsureInstalledAsync());
            Assert.Equal(ErrorCodes.UniDicDictionaryMissing, exception.ErrorCode);
        }

        Assert.Equal(valid, installer.IsInstalled);
        Assert.False(Directory.Exists(cacheRoot));
    }

    [Fact]
    public async Task Offline_install_reports_ready_only_after_validation_extraction_and_promotion()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixture);
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

    [Fact]
    public async Task Offline_archive_installs_atomically_and_writes_manifest()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixture);

        await installer.InstallFromArchiveAsync(fixture.ZipPath);

        Assert.True(installer.IsInstalled);
        foreach (var file in UniDicAssets.RequiredRuntimeFiles)
            Assert.True(File.Exists(Path.Combine(installer.DictionaryDirectory, file)));
        Assert.True(File.Exists(Path.Combine(installer.DictionaryDirectory, UniDicAssets.ManifestFileName)));
        Assert.Equal(installer.DictionaryDirectory, Assert.Single(Directory.EnumerateDirectories(cacheRoot)));
    }

    [Fact]
    public async Task Already_installed_cache_does_not_need_the_bundled_archive()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        await CreateInstaller(cacheRoot, fixture).EnsureInstalledAsync();
        File.Delete(fixture.ZipPath);
        var installer = CreateInstaller(cacheRoot, fixture);

        await installer.EnsureInstalledAsync();

        Assert.True(installer.IsInstalled);
    }

    [Fact]
    public async Task Corrupted_bundled_archive_is_rejected_before_installation()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        await File.AppendAllTextAsync(fixture.ZipPath, "corruption");
        var installer = CreateInstaller(cacheRoot, fixture);

        var exception = await Assert.ThrowsAsync<WindowsPlatformException>(() => installer.EnsureInstalledAsync());

        Assert.Equal(ErrorCodes.UniDicDictionaryInvalid, exception.ErrorCode);
        Assert.False(installer.IsInstalled);
        Assert.Empty(Directory.EnumerateFileSystemEntries(cacheRoot));
    }

    [Fact]
    public async Task Hash_mismatch_throws_dictionary_invalid_and_does_not_install()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        var installer = new UniDicDictionaryInstaller(fixture.ZipPath, cacheRoot, Manifest(new string('0', 64)));

        var exception = await Assert.ThrowsAsync<WindowsPlatformException>(
            () => installer.InstallFromArchiveAsync(fixture.ZipPath));

        Assert.Equal(ErrorCodes.UniDicDictionaryInvalid, exception.ErrorCode);
        Assert.False(installer.IsInstalled);
        Assert.False(Directory.Exists(installer.DictionaryDirectory));
    }

    [Fact]
    public async Task Archive_missing_runtime_files_throws_dictionary_invalid()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: false);
        var installer = CreateInstaller(cacheRoot, fixture);

        var exception = await Assert.ThrowsAsync<WindowsPlatformException>(() => installer.EnsureInstalledAsync());

        Assert.Equal(ErrorCodes.UniDicDictionaryInvalid, exception.ErrorCode);
        Assert.False(installer.IsInstalled);
    }

    [Fact]
    public async Task Cancelled_install_propagates_and_leaves_no_dictionary()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixture);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<DictionaryInstallProgress>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.EnsureInstalledAsync(
            ct: cancellation.Token, stages: progress));

        Assert.False(Directory.Exists(installer.DictionaryDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cacheRoot));
        Assert.True(File.Exists(fixture.ZipPath));
    }

    [Fact]
    public async Task Concurrent_first_run_installs_never_expose_partial_dictionary()
    {
        var (cacheRoot, fixture) = CreateFixture(populate: true);
        var installer = CreateInstaller(cacheRoot, fixture);

        await Task.WhenAll(installer.EnsureInstalledAsync(), installer.EnsureInstalledAsync(), installer.EnsureInstalledAsync());

        Assert.True(installer.IsInstalled);
        Assert.Equal(installer.DictionaryDirectory, Assert.Single(Directory.EnumerateDirectories(cacheRoot)));
    }

    private static UniDicDictionaryInstaller CreateInstaller(string cacheRoot, (string ZipPath, string Sha) fixture)
        => new(fixture.ZipPath, cacheRoot, Manifest(fixture.Sha));

    private static UniDicManifest Manifest(string sha)
        => new(UniDicAssets.Version, "https://example.test/unidic.zip", sha, UniDicAssets.Format, UniDicAssets.RequiredRuntimeFiles);

    private (string CacheRoot, (string ZipPath, string Sha) Fixture) CreateFixture(bool populate)
    {
        var cacheRoot = NewDir();
        var buildDir = NewDir();
        var dicDir = Path.Combine(buildDir, "dicdir");
        Directory.CreateDirectory(dicDir);
        if (populate)
        {
            foreach (var file in UniDicAssets.RequiredRuntimeFiles)
                File.WriteAllText(Path.Combine(dicDir, file), $"{file}-data");
            File.WriteAllText(Path.Combine(dicDir, UniDicAssets.DicrcFileName), $"output-format-type = {UniDicAssets.Format}");
            var licenses = Directory.CreateDirectory(Path.Combine(dicDir, "licenses"));
            File.WriteAllText(Path.Combine(licenses.FullName, "BSD"), "Fixture license notice.");
        }

        var zipPath = Path.Combine(NewDir(), UniDicAssets.ArchiveFileName);
        ZipFile.CreateFromDirectory(buildDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        using var stream = File.OpenRead(zipPath);
        return (cacheRoot, (zipPath, Convert.ToHexStringLower(SHA256.HashData(stream))));
    }

    private string NewDir()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ksins_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirs.Add(directory);
        return directory;
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirs)
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

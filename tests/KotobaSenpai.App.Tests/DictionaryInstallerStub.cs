using KotobaSenpai.Core.Japanese;

namespace KotobaSenpai.App.Tests;

internal sealed class DictionaryInstallerStub(Func<CancellationToken, Task>? install = null) : IUniDicDictionaryInstaller
{
    public bool IsInstalled { get; private set; }

    public async Task EnsureInstalledAsync(IProgress<double>? progress = null, CancellationToken ct = default,
        IProgress<DictionaryInstallProgress>? stages = null)
    {
        if (install is not null) await install(ct);
        ct.ThrowIfCancellationRequested();
        IsInstalled = true;
        stages?.Report(new(DictionaryInstallStage.Ready));
    }
}

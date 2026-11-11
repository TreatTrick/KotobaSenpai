namespace KotobaSenpai.Core.Japanese;

public enum DictionaryInstallStage { Validating, Extracting, Installing, Ready }
public sealed record DictionaryInstallProgress(DictionaryInstallStage Stage);

/// <summary>Prepares the effective runtime dictionary and reports installation progress without exposing storage or transport.</summary>
public interface IUniDicDictionaryInstaller
{
    bool IsInstalled { get; }

    Task EnsureInstalledAsync(CancellationToken ct = default,
        IProgress<DictionaryInstallProgress>? stages = null);
}

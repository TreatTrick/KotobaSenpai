namespace KotobaSenpai.Core.Japanese;

public enum DictionaryInstallStage { Downloading, Validating, Extracting, Installing, Ready }
public sealed record DictionaryInstallProgress(DictionaryInstallStage Stage, double? Fraction = null);

/// <summary>Prepares the effective runtime dictionary and reports installation progress without exposing storage or transport.</summary>
public interface IUniDicDictionaryInstaller
{
    bool IsInstalled { get; }

    Task EnsureInstalledAsync(IProgress<double>? progress = null, CancellationToken ct = default,
        IProgress<DictionaryInstallProgress>? stages = null);
}

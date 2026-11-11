namespace KotobaSenpai.Core.Updates;

/// <summary>Hands a verified update to the native installer; the caller then closes normally.</summary>
public interface IAppUpdateInstaller
{
    bool IsAvailable { get; }
    Task LaunchAsync(DownloadedAppUpdate update, CancellationToken cancellationToken);
}

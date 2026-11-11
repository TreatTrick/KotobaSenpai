namespace KotobaSenpai.Core.Updates;

/// <summary>Reads official releases and downloads a verified installation package.</summary>
public interface IAppUpdateSource
{
    Task<AppRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken);
    Task<DownloadedAppUpdate> DownloadAsync(AppRelease release, IProgress<AppUpdateProgress> progress,
        CancellationToken cancellationToken);
}

public sealed record AppRelease(string Tag, Version Version, string ReleaseNotes, Uri DownloadUri, long Size, string Sha256)
{
    public string InstallerName => $"KotobaSenpai-{Version.ToString(3)}-win-x64-Setup.exe";
}

public sealed record AppUpdateProgress(long BytesReceived, long TotalBytes, bool IsVerifying = false);

public sealed record DownloadedAppUpdate(AppRelease Release, string InstallerPath);

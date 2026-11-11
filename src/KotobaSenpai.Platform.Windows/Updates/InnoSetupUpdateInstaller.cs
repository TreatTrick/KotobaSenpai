using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Updates;

namespace KotobaSenpai.Platform.Windows.Updates;

/// <summary>Starts the existing per-user installer without replacing files in the running process.</summary>
public sealed class InnoSetupUpdateInstaller : IAppUpdateInstaller
{
    private readonly string _installDirectory;
    private readonly Func<string?> _registeredDirectory;
    private readonly Action<ProcessStartInfo> _start;

    public InnoSetupUpdateInstaller(string installDirectory)
        : this(installDirectory, ReadRegisteredDirectory, info =>
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("The update installer did not start.");
        }) { }

    internal InnoSetupUpdateInstaller(string installDirectory, Func<string?> registeredDirectory, Action<ProcessStartInfo> start)
    {
        _installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        _registeredDirectory = registeredDirectory;
        _start = start;
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return _registeredDirectory() is { Length: > 0 } path
                    && Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Equals(_installDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
            {
                return false;
            }
        }
    }

    public async Task LaunchAsync(DownloadedAppUpdate update, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            throw new AppUpdateException(ErrorCodes.AppUpdateInstallUnavailable, "The running application does not match the registered installation directory.");
        // Keep a read-only handle until process creation so the verified executable cannot be overwritten during handoff.
        await using var package = await UpdatePackage.OpenVerifiedAsync(update, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(Path.GetFullPath(update.InstallerPath)) { UseShellExecute = false };
        foreach (var argument in new[] { "/SILENT", "/SP-", "/NORESTART", "/CLOSEAPPLICATIONS", "/NOFORCECLOSEAPPLICATIONS",
            "/APPUPDATE=1", $"/UPDATEPID={Environment.ProcessId}", "/DIR=" + _installDirectory })
            info.ArgumentList.Add(argument);
        _start(info);
    }

    private static string? ReadRegisteredDirectory()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A32009BB-E6D3-49E5-A793-E0B1F2F51C11}_is1");
        return key?.GetValue("InstallLocation") as string;
    }
}

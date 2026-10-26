using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using KotobaSenpai.Core.Llm;

namespace KotobaSenpai.Platform.Windows.Llm;

/// <summary>Closes and restores the GUI around a configuration callback executed in the caller's process.</summary>
public sealed class ConfigurationProcessRunner(string appPath, string? ownershipLockName = null)
{
    public async Task<AgentApiResult> ExecuteAsync(string command, AgentApiRequest request,
        Func<CancellationToken, Task<AgentApiResult>> execute, CancellationToken cancellationToken = default)
    {
        if (AgentApiContract.Validate(command, request) is { } invalid)
            return invalid;
        if (cancellationToken.IsCancellationRequested)
            return AgentApiResult.Failure("cancelled", 130);
        var writes = command == "configure";
        var restoreGui = false;
        AgentApiResult? result = null;
        try
        {
            if (writes)
            {
                var windows = FindRunningGui();
                try
                {
                    foreach (var process in windows)
                    {
                        if (!process.CloseMainWindow())
                        {
                            result = AgentApiResult.Failure("app_busy", 4);
                            break;
                        }
                        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                        catch (TimeoutException)
                        {
                            result = AgentApiResult.Failure("app_busy", 4);
                            break;
                        }
                        restoreGui = true;
                    }
                }
                finally { windows.ForEach(process => process.Dispose()); }
            }
            if (result is null)
            {
                if (writes)
                {
                    // A named Mutex must be acquired and released on the same thread, even when the callback awaits HTTP.
                    result = await Task.Factory.StartNew(() =>
                    {
                        using var lease = ConfigurationProcessLock.TryAcquire(ownershipLockName);
                        return lease is null ? AgentApiResult.Failure("app_busy", 4)
                            : ExecuteSafelyAsync(execute, cancellationToken, writes: true).GetAwaiter().GetResult();
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                else
                    result = await ExecuteSafelyAsync(execute, cancellationToken, writes: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            result = AgentApiResult.Failure("gui_lifecycle_failed", 5);
        }
        if (restoreGui)
        {
            var restored = await RestoreGuiAsync();
            result = result! with
            {
                UiRestore = restored,
                NextAction = restored is "failed" or "deferred"
                    ? "The GUI could not be restored. Start KotobaSenpai manually after the settings owner exits; do not repeat a completed save."
                    : result!.NextAction,
            };
        }
        return result!;
    }

    private static async Task<AgentApiResult> ExecuteSafelyAsync(
        Func<CancellationToken, Task<AgentApiResult>> execute, CancellationToken cancellationToken, bool writes)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await execute(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return AgentApiResult.Failure("cancelled", 130); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return AgentApiResult.Failure("storage_failed", 5); }
        catch (Exception)
        {
            return AgentApiResult.Failure("configuration_failed", 5) with
            {
                Applied = writes ? null : false,
                NextAction = "Query status before retrying.",
            };
        }
    }

    private List<Process> FindRunningGui()
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(appPath)))
        {
            try
            {
                if (process.Id != Environment.ProcessId && process.MainWindowHandle != nint.Zero
                    && string.Equals(process.MainModule?.FileName, Path.GetFullPath(appPath), StringComparison.OrdinalIgnoreCase)
                    && OpenProcessToken(process.Handle, 8, out var token))
                {
                    using (token)
                    using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                    using (var current = WindowsIdentity.GetCurrent())
                        if (current.User is { } currentUser && identity.User?.Equals(currentUser) == true)
                        {
                            result.Add(process);
                            continue;
                        }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        return result;
    }

    private async Task<string> RestoreGuiAsync()
    {
        try
        {
            var existing = FindRunningGui();
            if (existing.Count > 0)
            {
                existing.ForEach(process => process.Dispose());
                return "already_running";
            }
            using (var lease = ConfigurationProcessLock.TryAcquire(ownershipLockName))
                if (lease is null)
                    return "deferred";
            using var gui = Process.Start(new ProcessStartInfo(Path.GetFullPath(appPath))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(appPath))! });
            if (gui is null)
                return "failed";
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                gui.Refresh();
                if (gui.HasExited)
                    return "failed";
                if (gui.MainWindowHandle != nint.Zero)
                    return "restored";
                await Task.Delay(50);
            }
            return "failed";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return "failed"; }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);
}

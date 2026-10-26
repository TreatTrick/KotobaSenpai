using System.Diagnostics;
using System.IO;
using KotobaSenpai.Core.Llm;
using KotobaSenpai.Platform.Windows.Llm;

namespace KotobaSenpai.App.Tests;

[CollectionDefinition("Configuration processes", DisableParallelization = true)]
public sealed class ConfigurationProcessCollection;

[Collection("Configuration processes")]
public sealed class ConfigurationProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ks-process-" + Guid.NewGuid().ToString("N"));
    private readonly string _lockName = @"Local\KotobaSenpai.Test." + Guid.NewGuid().ToString("N");
    private readonly string? _previousDirectory = Environment.GetEnvironmentVariable("KOTOBA_PROCESS_TEST_DIRECTORY");
    private readonly string? _previousLock = Environment.GetEnvironmentVariable("KOTOBA_PROCESS_TEST_LOCK");
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "KotobaSenpai.App.Tests.exe");
    private ConfigurationProcessRunner Runner => new(FixturePath, _lockName);
    private static AgentApiRequest Request => new()
        { SchemaVersion = 1, Endpoint = "https://api.example.com/v1", ApiKey = "test-key", Model = "test-model" };

    public ConfigurationProcessTests()
    {
        Directory.CreateDirectory(_directory);
        SetMode("success");
        Environment.SetEnvironmentVariable("KOTOBA_PROCESS_TEST_DIRECTORY", _directory);
        Environment.SetEnvironmentVariable("KOTOBA_PROCESS_TEST_LOCK", _lockName);
    }

    [Fact]
    public async Task Read_only_command_leaves_the_existing_gui_running()
    {
        using var gui = await StartGuiAsync();
        var result = await ExecuteAsync("status");
        Assert.Equal(0, result.ExitCode);
        Assert.False(gui.HasExited);
        Assert.False(File.Exists(Path.Combine(_directory, "gui-closed")));
        Assert.False(File.Exists(Path.Combine(_directory, "applied")));
    }

    [Theory]
    [InlineData("success", true, 0)]
    [InlineData("provider_failure", false, 3)]
    [InlineData("storage_failure", false, 5)]
    public async Task Write_executes_in_the_callers_process_and_restores_the_originally_open_gui(string mode, bool applied, int exitCode)
    {
        using var gui = await StartGuiAsync();
        SetMode(mode);
        var result = await ExecuteAsync();
        Assert.True(gui.HasExited);
        Assert.True(File.Exists(Path.Combine(_directory, "gui-closed")));
        Assert.Equal(Environment.ProcessId.ToString(), File.ReadAllText(Path.Combine(_directory, "operation-ready")));
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(applied, result.Applied);
        Assert.Equal("restored", result.UiRestore);
    }

    [Fact]
    public async Task Unexpected_failure_reports_an_unconfirmed_result_and_restores_the_gui()
    {
        using var gui = await StartGuiAsync();
        SetMode("unexpected_failure");
        var result = await ExecuteAsync();
        Assert.Equal(5, result.ExitCode);
        Assert.Null(result.Applied);
        Assert.Equal("restored", result.UiRestore);
    }

    [Fact]
    public async Task Originally_closed_gui_is_not_opened_and_writes_do_not_require_an_app_executable()
    {
        var runner = new ConfigurationProcessRunner(Path.Combine(_directory, "missing-app.exe"), _lockName);
        var result = await runner.ExecuteAsync("configure", Request, token => ExecuteConfigurationAsync("configure", token));
        Assert.True(result.Applied);
        Assert.Equal("not_needed", result.UiRestore);
        Assert.False(File.Exists(Path.Combine(_directory, "gui-ready")));
    }

    [Fact]
    public async Task Failed_gui_restoration_does_not_misreport_a_completed_save()
    {
        using var gui = await StartGuiAsync();
        SetMode("restore_failure");
        var result = await ExecuteAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Equal("failed", result.UiRestore);
    }

    [Fact]
    public async Task Invalid_input_does_not_close_the_gui_or_execute_the_callback()
    {
        using var gui = await StartGuiAsync();
        var result = await Runner.ExecuteAsync("configure", Request with { ApiKey = null },
            token => ExecuteConfigurationAsync("configure", token));
        Assert.Equal("missing_api_key", result.Code);
        Assert.False(gui.HasExited);
        Assert.False(File.Exists(Path.Combine(_directory, "operation-ready")));
    }

    [Fact]
    public async Task Exit_timeout_does_not_kill_the_gui_or_execute_configuration()
    {
        using var gui = await StartGuiAsync();
        SetMode("ignore_close");
        var result = await ExecuteAsync();
        Assert.Equal(4, result.ExitCode);
        Assert.False(gui.HasExited);
        Assert.False(File.Exists(Path.Combine(_directory, "operation-ready")));
    }

    [Fact]
    public async Task Concurrent_cli_and_gui_cannot_acquire_a_second_write_owner()
    {
        SetMode("wait_for_cancel");
        using var cancellation = new CancellationTokenSource();
        var first = ExecuteAsync(cancellationToken: cancellation.Token);
        for (var attempt = 0; attempt < 100 && !File.Exists(Path.Combine(_directory, "operation-ready")); attempt++)
            await Task.Delay(50);
        Assert.True(File.Exists(Path.Combine(_directory, "operation-ready")));
        try
        {
            var second = await ExecuteAsync();
            Assert.Equal(4, second.ExitCode);
            using var gui = Process.Start(new ProcessStartInfo(FixturePath)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })!;
            await gui.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, gui.ExitCode);
        }
        finally { cancellation.Cancel(); }
        Assert.Equal(130, (await first).ExitCode);
        SetMode("success");
        Assert.True((await ExecuteAsync()).Applied);
    }

    [Fact]
    public async Task Cancellation_releases_the_mutex_after_async_work_and_restores_the_gui()
    {
        using var gui = await StartGuiAsync();
        SetMode("wait_for_cancel");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await ExecuteAsync(cancellationToken: cancellation.Token);
        Assert.Equal(130, result.ExitCode);
        Assert.False(result.Applied);
        Assert.Equal("restored", result.UiRestore);
        Assert.False(File.Exists(Path.Combine(_directory, "applied")));
    }

    [Fact]
    public async Task Cancellation_after_commit_keeps_the_successful_result_and_restores_the_gui()
    {
        using var gui = await StartGuiAsync();
        using var cancellation = new CancellationTokenSource();
        var result = await Runner.ExecuteAsync("configure", Request, _ =>
        {
            File.WriteAllText(Path.Combine(_directory, "applied"), "configure");
            cancellation.Cancel();
            return Task.FromResult(new AgentApiResult { Applied = true });
        }, cancellation.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Applied);
        Assert.Equal("restored", result.UiRestore);
    }

    private Task<AgentApiResult> ExecuteAsync(string command = "configure", CancellationToken cancellationToken = default)
        => Runner.ExecuteAsync(command, command == "status" ? new() { SchemaVersion = 1 } : Request,
            token => ExecuteConfigurationAsync(command, token), cancellationToken);

    private async Task<AgentApiResult> ExecuteConfigurationAsync(string command, CancellationToken cancellationToken)
    {
        File.WriteAllText(Path.Combine(_directory, "operation-ready"), Environment.ProcessId.ToString());
        var mode = File.ReadAllText(Path.Combine(_directory, "mode"));
        if (mode == "wait_for_cancel")
            await Task.Delay(Timeout.Infinite, cancellationToken);
        if (mode == "storage_failure")
            throw new IOException("The test settings file is not writable.");
        if (mode == "unexpected_failure")
            throw new InvalidOperationException("The test callback failed unexpectedly.");
        if (mode == "provider_failure")
            return AgentApiResult.Failure("invalid_analysis");
        var writes = command == "configure";
        if (writes)
            File.WriteAllText(Path.Combine(_directory, "applied"), command);
        return new() { Applied = writes };
    }

    private async Task<Process> StartGuiAsync()
    {
        var gui = Process.Start(new ProcessStartInfo(FixturePath)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })!;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            gui.Refresh();
            if (File.Exists(Path.Combine(_directory, "gui-ready")) && gui.MainWindowHandle != nint.Zero)
                return gui;
            await Task.Delay(50);
        }
        throw new InvalidOperationException("The lifecycle fixture GUI did not become ready.");
    }

    private void SetMode(string mode) => File.WriteAllText(Path.Combine(_directory, "mode"), mode);

    public void Dispose()
    {
        SetMode("success");
        foreach (var process in Process.GetProcessesByName("KotobaSenpai.App.Tests"))
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != nint.Zero && process.MainModule?.FileName == FixturePath)
                    {
                        process.CloseMainWindow();
                        process.WaitForExit(3000);
                    }
                }
                catch (InvalidOperationException) { }
            }
        }
        Environment.SetEnvironmentVariable("KOTOBA_PROCESS_TEST_DIRECTORY", _previousDirectory);
        Environment.SetEnvironmentVariable("KOTOBA_PROCESS_TEST_LOCK", _previousLock);
        foreach (var path in Directory.EnumerateFiles(_directory))
            File.Delete(path);
        Directory.Delete(_directory);
    }
}

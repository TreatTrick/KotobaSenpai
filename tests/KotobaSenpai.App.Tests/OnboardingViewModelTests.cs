using KotobaSenpai.App.Onboarding;
using System.IO;
using KotobaSenpai.App.Localization;
using KotobaSenpai.App.ViewModels;
using KotobaSenpai.TestSupport;

namespace KotobaSenpai.App.Tests;

public sealed class OnboardingViewModelTests
{
    [Fact]
    public async Task An_active_step_ignores_duplicate_initialization()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "model");
        var preparations = 0;
        using var vm = Create(settings, () => new(true, true), _ =>
        {
            preparations++;
            return Task.CompletedTask;
        });

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(1, preparations);
        Assert.Equal(OnboardingStep.Model, vm.Step);
    }

    [Theory]
    [InlineData(null, false, false, OnboardingStep.Dictionary, "dictionary")]
    [InlineData(null, true, false, OnboardingStep.Model, "model")]
    [InlineData(null, true, true, OnboardingStep.Main, "completed")]
    [InlineData("model", true, true, OnboardingStep.Model, "model")]
    [InlineData("window", true, true, OnboardingStep.Window, "window")]
    [InlineData("window", true, false, OnboardingStep.Model, "model")]
    [InlineData("window", false, true, OnboardingStep.Dictionary, "dictionary")]
    [InlineData("invalid", true, true, OnboardingStep.Model, "model")]
    [InlineData("completed", true, false, OnboardingStep.Main, "completed")]
    public async Task Startup_restores_only_valid_checkpoints(string? checkpoint, bool dictionary, bool model,
        OnboardingStep expectedStep, string expectedCheckpoint)
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", checkpoint);
        settings.SetValue("Theme", "Dark");
        var readiness = new OnboardingReadiness(dictionary, model);
        using var vm = Create(settings, () => readiness);

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(expectedStep, vm.Step);
        Assert.Equal(expectedCheckpoint, settings.GetValue("OnboardingState"));
        Assert.Equal("Dark", settings.GetValue("Theme"));
    }

    [Fact]
    public async Task Startup_saves_a_checkpoint_before_preparation_and_supports_retry()
    {
        var settings = new MemorySettings();
        var attempts = 0;
        using var vm = Create(settings, () => new(false, false), _ =>
        {
            Assert.Equal("dictionary", settings.GetValue("OnboardingState"));
            if (++attempts == 1) throw new InvalidOperationException("Preparation failed.");
            return Task.CompletedTask;
        });

        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.NextCommand.CanExecute(null));
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Navigation_returns_one_step_without_requiring_a_selected_target()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "model");
        var readiness = new OnboardingReadiness(true, true);
        using var vm = Create(settings, () => readiness);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.False(vm.BackCommand.CanExecute(null));

        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Window, vm.Step);
        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.True(vm.BackCommand.CanExecute(null));
        vm.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Model, vm.Step);
        Assert.Equal("model", settings.GetValue("OnboardingState"));

        readiness = readiness with { HasTarget = true, TargetReady = true };
        vm.Refresh();
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Ready, vm.Step);
        vm.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Window, vm.Step);
        Assert.Equal("window", settings.GetValue("OnboardingState"));
    }

    [Fact]
    public async Task Model_changes_and_pending_operations_prevent_forward_navigation()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "model");
        var readiness = new OnboardingReadiness(true, false);
        using var vm = Create(settings, () => readiness);
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.False(vm.NextCommand.CanExecute(null));
        readiness = readiness with { ModelReady = true, ModelBusy = true };
        vm.Refresh();
        Assert.False(vm.NextCommand.CanExecute(null));
        readiness = readiness with { ModelBusy = false };
        vm.Refresh();
        await vm.NextCommand.ExecuteAsync(null);
        readiness = readiness with { TargetBusy = true };
        vm.Refresh();
        Assert.False(vm.BackCommand.CanExecute(null));
        Assert.False(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Handoff_saves_completion_before_one_recognition_request()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "window");
        var requests = 0;
        using var vm = Create(settings, () => new(true, true, true, true), start: () =>
        {
            Assert.Equal("completed", settings.GetValue("OnboardingState"));
            requests++;
            return Task.CompletedTask;
        });
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Equal(OnboardingStep.Main, vm.Step);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Failed_checkpoint_save_does_not_navigate_or_start_recognition()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "window");
        var requests = 0;
        using var vm = Create(settings, () => new(true, true, true, true), start: () =>
        {
            requests++;
            return Task.CompletedTask;
        });
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        settings.FailWrites = true;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Ready, vm.Step);
        Assert.True(vm.HasError);
        Assert.Equal(0, requests);
        settings.FailWrites = false;
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Closed_target_returns_to_selection_without_losing_the_checkpoint()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "window");
        var readiness = new OnboardingReadiness(true, true, true, true);
        using var vm = Create(settings, () => readiness);
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        readiness = readiness with { HasTarget = false, TargetReady = false };
        vm.Refresh();
        Assert.Equal(OnboardingStep.Window, vm.Step);
        Assert.Equal("window", settings.GetValue("OnboardingState"));
    }

    [Fact]
    public async Task Completed_users_repair_dictionary_without_reopening_onboarding()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "completed");
        var dictionaryReady = false;
        using var vm = Create(settings, () => new(dictionaryReady, false), _ =>
        {
            dictionaryReady = true;
            return Task.CompletedTask;
        });
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Main, vm.Step);
        Assert.Equal("completed", settings.GetValue("OnboardingState"));
    }

    [Fact]
    public async Task Invalidated_dictionary_returns_to_preparation_with_a_retry_entry()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "model");
        var readiness = new OnboardingReadiness(true, true);
        using var vm = Create(settings, () => readiness);
        await vm.InitializeCommand.ExecuteAsync(null);
        readiness = readiness with { DictionaryReady = false };
        vm.Refresh();
        Assert.Equal(OnboardingStep.Dictionary, vm.Step);
        Assert.True(vm.ShowRetry);
        Assert.False(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Failed_back_save_keeps_the_current_step_and_checkpoint()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "window");
        using var vm = Create(settings, () => new(true, true));
        await vm.InitializeCommand.ExecuteAsync(null);
        settings.FailWrites = true;
        vm.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Window, vm.Step);
        Assert.Equal("window", settings.GetValue("OnboardingState"));
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task Handoff_rechecks_target_identity_before_saving_completion()
    {
        var settings = new MemorySettings();
        settings.SetValue("OnboardingState", "window");
        var readiness = new OnboardingReadiness(true, true, true, true);
        var refreshes = 0;
        using var vm = new OnboardingViewModel(settings, new ResourceManagerStringLocalizer(), () => readiness,
            _ => Task.CompletedTask, () => throw new InvalidOperationException("Recognition must not start."),
            () => { if (++refreshes == 2) readiness = readiness with { HasTarget = false, TargetReady = false }; });
        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(OnboardingStep.Window, vm.Step);
        Assert.Equal("window", settings.GetValue("OnboardingState"));
    }

    [Fact]
    public async Task Disposal_cancels_preparation_without_replaying_it()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Create(new MemorySettings(), () => new(false, false), async cancellation =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { cancelled.SetResult(); }
        });
        var initializing = vm.InitializeCommand.ExecuteAsync(null);
        await entered.Task;
        vm.Dispose();
        await initializing;
        Assert.True(cancelled.Task.IsCompleted);
        Assert.NotEqual(OnboardingStep.Main, vm.Step);
    }

    private static OnboardingViewModel Create(MemorySettings settings, Func<OnboardingReadiness> readiness,
        Func<CancellationToken, Task>? prepare = null, Func<Task>? start = null)
        => new(settings, new ResourceManagerStringLocalizer(), readiness,
            prepare ?? (_ => Task.CompletedTask), start ?? (() => Task.CompletedTask));

    private sealed class MemorySettings : SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public bool FailWrites { get; set; }
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value)
        {
            if (FailWrites) throw new IOException("Settings could not be saved.");
            _values[key] = value;
        }
    }
}

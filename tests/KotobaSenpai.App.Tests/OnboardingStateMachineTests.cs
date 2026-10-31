using KotobaSenpai.App.Onboarding;
using KotobaSenpai.TestSupport;

namespace KotobaSenpai.App.Tests;

public sealed class OnboardingStateMachineTests
{
    [Fact]
    public async Task Pending_preparation_rejects_navigation_and_duplicate_initialization()
    {
        var settings = new MemorySettings("model");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var machine = new OnboardingStateMachine(settings, () => new(true, true), _ =>
        {
            calls++;
            return pending.Task;
        }, () => Task.CompletedTask);

        var initializing = machine.InitializeAsync();
        Assert.Equal(new(OnboardingPhase.Preparing, OnboardingStep.Model), machine.State);
        Assert.True(machine.IsBusy);
        Assert.False(machine.CanHandle(OnboardingAction.Next));
        Assert.False(machine.CanHandle(OnboardingAction.Back));
        await machine.InitializeAsync();
        Assert.Equal(1, calls);

        pending.SetResult();
        await initializing;
        Assert.Equal(new(OnboardingPhase.Active, OnboardingStep.Model), machine.State);
        Assert.False(machine.IsBusy);
        Assert.False(machine.CanInitialize);
        Assert.True(machine.CanHandle(OnboardingAction.Next));
        Assert.False(machine.CanHandle((OnboardingAction)999));
    }

    [Fact]
    public async Task Cancelled_preparation_keeps_a_retryable_phase_and_checkpoint()
    {
        var settings = new MemorySettings("model");
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var machine = new OnboardingStateMachine(settings, () => new(true, true), token =>
            ++calls == 1 ? Task.Delay(Timeout.Infinite, token) : Task.CompletedTask, () => Task.CompletedTask);

        var initializing = machine.InitializeAsync(cancellation.Token);
        cancellation.Cancel();
        await initializing;
        Assert.Equal(OnboardingPhase.PreparationFailed, machine.State.Phase);
        Assert.Equal("model", settings.GetValue(OnboardingStateMachine.CheckpointKey));
        Assert.True(machine.CanInitialize);
        Assert.False(machine.CanHandle(OnboardingAction.Next));

        await machine.InitializeAsync();
        Assert.Equal(new(OnboardingPhase.Active, OnboardingStep.Model), machine.State);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Repair_failure_keeps_completed_intent_when_retried()
    {
        var settings = new MemorySettings("completed");
        var attempts = 0;
        using var machine = new OnboardingStateMachine(settings, () => new(false, false), _ =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Preparation failed.");
            return Task.CompletedTask;
        }, () => throw new InvalidOperationException("Recognition must not start during repair."));

        await machine.InitializeAsync();
        Assert.Equal(OnboardingPhase.RepairFailed, machine.State.Phase);
        Assert.False(machine.IsGuideVisible);
        Assert.True(machine.CanInitialize);
        Assert.Equal("completed", settings.GetValue(OnboardingStateMachine.CheckpointKey));

        await machine.InitializeAsync();
        Assert.Equal(new(OnboardingPhase.Main, OnboardingStep.Main), machine.State);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Starting_commits_completion_and_rejects_all_navigation(bool failRecognition)
    {
        var settings = new MemorySettings("window");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var machine = new OnboardingStateMachine(settings, () => new(true, true, true, true),
            _ => Task.CompletedTask, () =>
            {
                Assert.Equal("completed", settings.GetValue(OnboardingStateMachine.CheckpointKey));
                requests++;
                return pending.Task;
            });
        await machine.InitializeAsync();
        await machine.NextAsync();
        var starting = machine.NextAsync();
        Assert.Equal(new(OnboardingPhase.Starting, OnboardingStep.Main), machine.State);
        Assert.True(machine.IsMain);
        Assert.True(machine.IsBusy);
        Assert.False(machine.CanInitialize);
        machine.Back();
        await machine.NextAsync();
        await machine.InitializeAsync();
        Assert.Equal(1, requests);

        if (failRecognition) pending.SetException(new InvalidOperationException("Recognition failed."));
        else pending.SetResult();
        await starting;
        Assert.Equal(OnboardingPhase.Main, machine.State.Phase);
        Assert.Equal(failRecognition ? "Onboarding_RecognitionFailed" : null, machine.State.ErrorKey);
        Assert.False(machine.IsBusy);
        Assert.Equal("completed", settings.GetValue(OnboardingStateMachine.CheckpointKey));
    }

    [Fact]
    public async Task Returning_from_a_preparation_failure_preserves_retry_instead_of_unlocking_forward_navigation()
    {
        var settings = new MemorySettings("window");
        var attempts = 0;
        using var machine = new OnboardingStateMachine(settings, () => new(true, true, true, true), _ =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Preparation failed.");
            return Task.CompletedTask;
        }, () => Task.CompletedTask);
        await machine.InitializeAsync();
        machine.Back();
        Assert.Equal(OnboardingStep.Model, machine.State.Step);
        Assert.Equal(OnboardingPhase.PreparationFailed, machine.State.Phase);
        Assert.True(machine.CanInitialize);
        Assert.False(machine.CanHandle(OnboardingAction.Next));
        Assert.Equal("model", settings.GetValue(OnboardingStateMachine.CheckpointKey));

        await machine.InitializeAsync();
        Assert.Equal(new(OnboardingPhase.Active, OnboardingStep.Model), machine.State);
    }

    [Fact]
    public async Task Closed_flow_ignores_late_preparation_and_never_reads_disposed_services()
    {
        var settings = new MemorySettings("model");
        var closed = false;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var machine = new OnboardingStateMachine(settings, () => closed
                ? throw new InvalidOperationException("Disposed capabilities must not be read.") : new(true, true),
            _ => pending.Task, () => throw new InvalidOperationException("Recognition must not start."));
        var initializing = machine.InitializeAsync();
        machine.Dispose();
        closed = true;
        machine.Refresh();
        machine.Back();
        await machine.NextAsync();
        await machine.InitializeAsync();
        Assert.False(machine.CanHandle(OnboardingAction.Next));
        Assert.False(machine.CanHandle(OnboardingAction.Back));
        pending.SetResult();
        await initializing;
        Assert.Equal(OnboardingPhase.Closed, machine.State.Phase);
        Assert.Equal(OnboardingStep.Model, machine.State.Step);
    }

    private sealed class MemorySettings(string checkpoint) : SettingsStub
    {
        private readonly Dictionary<string, string?> _values = new() { [OnboardingStateMachine.CheckpointKey] = checkpoint };
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value) => _values[key] = value;
    }
}

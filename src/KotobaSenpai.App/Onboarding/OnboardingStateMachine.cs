using System.IO;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Onboarding;

public enum OnboardingStep { Dictionary, Model, Window, Ready, Main }
public enum OnboardingPhase { NotStarted, Preparing, PreparationFailed, Repairing, RepairFailed, Active, Starting, Main, Closed }
public enum OnboardingAction { Next, Back }

public sealed record OnboardingReadiness(bool DictionaryReady, bool ModelReady,
    bool HasTarget = false, bool TargetReady = false, bool TargetBusy = false, bool ModelBusy = false);

public sealed record OnboardingState(OnboardingPhase Phase, OnboardingStep Step, string? ErrorKey = null);

/// <summary>Owns onboarding transitions, guards and effects without depending on presentation objects.</summary>
public sealed class OnboardingStateMachine(ISettingsService settings, Func<OnboardingReadiness> readiness,
    Func<CancellationToken, Task> prepare, Func<Task> start, Action? refreshTarget = null) : IDisposable
{
    public const string CheckpointKey = "OnboardingState";
    public OnboardingState State { get; private set; } = new(OnboardingPhase.NotStarted, OnboardingStep.Dictionary);
    public event Action? Changed;
    public bool IsBusy => State.Phase is OnboardingPhase.Preparing or OnboardingPhase.Repairing or OnboardingPhase.Starting;
    public bool IsMain => State.Step == OnboardingStep.Main;
    public bool IsGuideVisible => State.Phase is not (OnboardingPhase.Repairing or OnboardingPhase.RepairFailed
        or OnboardingPhase.Starting or OnboardingPhase.Main or OnboardingPhase.Closed);
    public bool CanInitialize => State.Phase is OnboardingPhase.NotStarted or OnboardingPhase.PreparationFailed or OnboardingPhase.RepairFailed;
    public bool IsPreparingRuntime => (State.Phase is OnboardingPhase.Preparing or OnboardingPhase.Repairing) && readiness().DictionaryReady;
    private bool IsClosed => State.Phase == OnboardingPhase.Closed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInitialize) return;
        SetState(State with { Phase = OnboardingPhase.Preparing, ErrorKey = null });
        try
        {
            var checkpoint = settings.GetValue(CheckpointKey);
            var ready = readiness();
            var migrate = checkpoint is null && ready.DictionaryReady && ready.ModelReady;
            var completed = checkpoint == "completed" || migrate;
            var step = ResolveStartupStep(checkpoint, ready);
            if (!completed) SaveStep(step);
            SetState(new(completed ? OnboardingPhase.Repairing : OnboardingPhase.Preparing,
                completed ? OnboardingStep.Dictionary : step));
            await prepare(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsClosed) return;
            if (migrate) SaveStep(OnboardingStep.Main);
            SetState(completed ? new(OnboardingPhase.Main, OnboardingStep.Main) : new(OnboardingPhase.Active, step));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FailPreparation("Onboarding_PreparationFailed");
        }
        catch (Exception exception)
        {
            FailPreparation(exception is IOException or UnauthorizedAccessException
                ? "Onboarding_SaveFailed" : "Onboarding_PreparationFailed");
        }
        Refresh();
    }

    private static OnboardingStep ResolveStartupStep(string? checkpoint, OnboardingReadiness ready)
        => !ready.DictionaryReady ? OnboardingStep.Dictionary
            : checkpoint == "window" && ready.ModelReady ? OnboardingStep.Window : OnboardingStep.Model;

    public bool CanHandle(OnboardingAction action)
        => (State.Phase is OnboardingPhase.Active or OnboardingPhase.PreparationFailed)
            && Destination(action, readiness()) is not null;

    private OnboardingStep? Destination(OnboardingAction action, OnboardingReadiness ready)
    {
        if (action == OnboardingAction.Back)
        {
            if (State.Phase is not (OnboardingPhase.Active or OnboardingPhase.PreparationFailed) || ready.TargetBusy) return null;
            return State.Step switch
            {
                OnboardingStep.Window => OnboardingStep.Model,
                OnboardingStep.Ready => OnboardingStep.Window,
                _ => null,
            };
        }
        if (action != OnboardingAction.Next || State.Phase != OnboardingPhase.Active || !ready.DictionaryReady) return null;
        return State.Step switch
        {
            OnboardingStep.Dictionary => OnboardingStep.Model,
            OnboardingStep.Model when ready.ModelReady && !ready.ModelBusy => OnboardingStep.Window,
            OnboardingStep.Window when CanUseTarget(ready) => OnboardingStep.Ready,
            OnboardingStep.Ready when CanUseTarget(ready) => OnboardingStep.Main,
            _ => null,
        };
    }

    private static bool CanUseTarget(OnboardingReadiness ready)
        => ready.ModelReady && !ready.ModelBusy && ready.HasTarget && ready.TargetReady && !ready.TargetBusy;

    public async Task NextAsync()
    {
        if (State.Phase != OnboardingPhase.Active) return;
        try
        {
            if (State.Step is OnboardingStep.Window or OnboardingStep.Ready) refreshTarget?.Invoke();
            if (!Move(OnboardingAction.Next) || State.Phase != OnboardingPhase.Starting) return;
            await start();
            if (!IsClosed) SetState(new(OnboardingPhase.Main, OnboardingStep.Main));
        }
        catch (Exception)
        {
            if (!IsClosed) SetState(State with
            {
                Phase = IsMain ? OnboardingPhase.Main : State.Phase,
                ErrorKey = IsMain ? "Onboarding_RecognitionFailed" : "Onboarding_SaveFailed",
            });
        }
        finally { Refresh(); }
    }

    public void Back()
    {
        Move(OnboardingAction.Back);
        Refresh();
    }

    private bool Move(OnboardingAction action)
    {
        if (State.Phase is not (OnboardingPhase.Active or OnboardingPhase.PreparationFailed)) return false;
        var next = Destination(action, readiness());
        if (next is null) return false;
        try
        {
            SaveStep(next.Value);
            if (IsClosed) return false;
            SetState(new(next == OnboardingStep.Main ? OnboardingPhase.Starting : State.Phase, next.Value,
                State.Phase == OnboardingPhase.PreparationFailed ? State.ErrorKey : null));
            return true;
        }
        catch (Exception)
        {
            if (!IsClosed) SetState(State with { ErrorKey = "Onboarding_SaveFailed" });
            return false;
        }
    }

    private void SaveStep(OnboardingStep step) => settings.SetValue(CheckpointKey, step switch
    {
        OnboardingStep.Dictionary => "dictionary",
        OnboardingStep.Model => "model",
        OnboardingStep.Window or OnboardingStep.Ready => "window",
        OnboardingStep.Main => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    });

    public void Refresh()
    {
        if (IsClosed) return;
        var ready = readiness();
        if (State.Phase == OnboardingPhase.Active && !ready.DictionaryReady && State.Step != OnboardingStep.Dictionary)
        {
            try
            {
                SaveStep(OnboardingStep.Dictionary);
                SetState(new(OnboardingPhase.PreparationFailed, OnboardingStep.Dictionary, "Onboarding_PreparationFailed"));
            }
            catch (Exception) { FailPreparation("Onboarding_SaveFailed"); }
        }
        else if (State.Phase == OnboardingPhase.Active && State.Step == OnboardingStep.Ready && !ready.HasTarget)
            SetState(new(OnboardingPhase.Active, OnboardingStep.Window));
        else
            Changed?.Invoke();
    }

    private void FailPreparation(string error)
    {
        if (IsClosed) return;
        SetState(State with
        {
            Phase = State.Phase is OnboardingPhase.Repairing or OnboardingPhase.RepairFailed
                ? OnboardingPhase.RepairFailed : OnboardingPhase.PreparationFailed,
            ErrorKey = error,
        });
    }

    private void SetState(OnboardingState state)
    {
        State = state;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (!IsClosed) SetState(State with { Phase = OnboardingPhase.Closed });
    }
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Onboarding;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.ViewModels;

public sealed record OnboardingProgressItem(string Label, string Marker, bool IsCurrent);

/// <summary>Adapts the onboarding state machine to commands and localized presentation properties.</summary>
public sealed partial class OnboardingViewModel : ObservableObject, IDisposable
{
    public const string StateKey = OnboardingStateMachine.CheckpointKey;
    private readonly IStringLocalizer _localizer;
    private readonly OnboardingStateMachine _flow;
    private OnboardingStep _presentedStep;

    public OnboardingViewModel(ISettingsService settings, IStringLocalizer localizer,
        Func<OnboardingReadiness> readiness, Func<CancellationToken, Task> prepare, Func<Task> start,
        Action? refreshTarget = null)
    {
        _localizer = localizer;
        _flow = new(settings, readiness, prepare, start, refreshTarget);
        _flow.Changed += NotifyPresentation;
        _localizer.CultureChanged += OnCultureChanged;
    }

    public OnboardingState State => _flow.State;
    public OnboardingStep Step => State.Step;
    public bool IsBusy => _flow.IsBusy;
    public bool IsMain => _flow.IsMain;
    public bool IsGuideVisible => _flow.IsGuideVisible;
    public bool ShowBack => Step is OnboardingStep.Window or OnboardingStep.Ready;
    public bool ShowRetry => State.Phase is OnboardingPhase.PreparationFailed or OnboardingPhase.RepairFailed;
    public bool IsPreparingRuntime => _flow.IsPreparingRuntime;
    public bool HasError => State.ErrorKey is not null;
    public string Error => State.ErrorKey is null ? string.Empty : _localizer.Get(State.ErrorKey);
    public string HeroTitle => _localizer.Get(IsGuideVisible ? "Onboarding_Hero" + ((int)Step + 1) : "Onboarding_Preparing");
    public string Description => _localizer.Get(IsGuideVisible ? "Onboarding_Description" + ((int)Step + 1) : "Onboarding_PreparingDescription");
    public string ProgressText => _localizer.Get("Onboarding_Progress", (int)Step + 1, 4);
    public string PrimaryActionText => _localizer.Get(Step == OnboardingStep.Ready ? "Onboarding_Start" : "Onboarding_Next");
    public IReadOnlyList<OnboardingProgressItem> Progress => Enumerable.Range(0, 4)
        .Select(index => new OnboardingProgressItem(_localizer.Get("Onboarding_Step" + (index + 1)),
            (int)Step > index ? "✓" : (index + 1).ToString(CultureInfo.InvariantCulture), (int)Step == index)).ToArray();

    private bool CanInitialize() => _flow.CanInitialize;
    [RelayCommand(CanExecute = nameof(CanInitialize))]
    private Task InitializeAsync(CancellationToken cancellationToken) => _flow.InitializeAsync(cancellationToken);
    private bool CanNext() => _flow.CanHandle(OnboardingAction.Next);
    [RelayCommand(CanExecute = nameof(CanNext))]
    private Task NextAsync() => _flow.NextAsync();
    private bool CanBack() => _flow.CanHandle(OnboardingAction.Back);
    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => _flow.Back();
    public void Refresh() => _flow.Refresh();

    private void NotifyPresentation()
    {
        foreach (var name in new[] { nameof(State), nameof(IsBusy), nameof(IsMain), nameof(IsGuideVisible),
            nameof(ShowBack), nameof(ShowRetry), nameof(IsPreparingRuntime), nameof(HasError), nameof(Error),
            nameof(HeroTitle), nameof(Description), nameof(ProgressText), nameof(PrimaryActionText), nameof(Progress) })
            OnPropertyChanged(name);
        if (_presentedStep != Step)
        {
            _presentedStep = Step;
            OnPropertyChanged(nameof(Step));
        }
        InitializeCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    private void OnCultureChanged(object? sender, EventArgs e) => NotifyPresentation();
    public void Dispose()
    {
        if (State.Phase == OnboardingPhase.Closed) return;
        _localizer.CultureChanged -= OnCultureChanged;
        _flow.Changed -= NotifyPresentation;
        InitializeCommand.Cancel();
        _flow.Dispose();
    }
}

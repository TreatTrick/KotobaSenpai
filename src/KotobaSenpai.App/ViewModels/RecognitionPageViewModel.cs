using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Reading;
using KotobaSenpai.App.Resources;
using KotobaSenpai.App.Support;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Workflow;

namespace KotobaSenpai.App.ViewModels;

/// <summary>Projects a reading session into localized page state and shared reading commands.</summary>
public sealed partial class RecognitionPageViewModel : ObservableObject, IDisposable
{
    private readonly RecognitionSessionController _session;
    private readonly IRegionSelector _region;
    private readonly IWindowFrameCapture _capture;
    private readonly ISettingsService _settings;
    private readonly IStringLocalizer _localizer;
    private readonly IUserMessageResolver _messages;
    private readonly ILogger _logger;
    private readonly SupportService _support;
    private Func<string> _renderStatus = () => string.Empty;
    private RecognitionPhase _previousPhase;
    private Exception? _shownError;
    private bool _disposed;

    public RecognitionPageViewModel(TargetSelectionViewModel selection, RecognitionSessionController session, ReadingActions actions,
        IRegionSelector regionSelector, ISettingsService settings, IStringLocalizer localizer,
        IUserMessageResolver messageResolver, ILogger logger, IWindowFrameCapture capture,
        SupportService support,
        ShortcutSettingsViewModel shortcuts)
    {
        Selection = selection;
        Shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _region = regionSelector;
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _settings = settings;
        _localizer = localizer;
        _messages = messageResolver;
        _logger = logger;
        _support = support ?? throw new ArgumentNullException(nameof(support));
        Selection.SelectionCommitted += OnSelectionCommitted;
        Selection.PropertyChanged += OnSelectionPropertyChanged;
        _region.SelectionStateChanged += OnRegionChanged;
        Selection.SetRegionSelectionActive(_region.IsSelecting);
        _session.Changed += OnSessionChanged;
        _session.Completed += OnCompleted;
        Actions.Notice += OnNotice;
        Actions.Failed += OnActionFailed;
        _localizer.CultureChanged += OnCultureChanged;
        SetStatus(ResourceKeys.Status_SelectTarget);
    }

    public TargetSelectionViewModel Selection { get; }
    public ReadingActions Actions { get; }
    public ShortcutSettingsViewModel Shortcuts { get; }
    public bool IsHidden => _session.State.Phase == RecognitionPhase.Hidden;
    public string VisibilityButtonText => _localizer.Get(IsHidden ? "Reading_Resume" : "Reading_HidePause");
    public string RunState => _localizer.Get(IsHidden ? "Reading_HiddenPaused"
        : _session.State.IsAutomaticRunning ? "Reading_AutoRunning" : "Reading_AutoOff");
    public bool AutoRecognitionEnabled
    {
        get => _session.State.AutoRecognitionEnabled;
        set { if (value != AutoRecognitionEnabled) _ = SetAutomaticAsync(value); }
    }

    [ObservableProperty] private string _status = string.Empty;
    public Task StartRecognitionAsync() => ExecuteAsync(ReadingAction.Recognize);
    public Task StopAsync() => _session.StopAsync();
    public void RefreshInteraction() => NotifyCommands();
    private bool CanRecognize() => !_disposed && Actions.CanExecute(ReadingAction.Recognize) && Selection.SelectedWindow is not null;
    private bool CanRegion() => !_disposed && Actions.CanExecute(ReadingAction.SelectRegion) && Selection.SelectedWindow is not null;
    private bool CanStop() => !_disposed && Actions.CanExecute(ReadingAction.Stop);
    private bool CanVisibility() => !_disposed && Actions.CanExecute(ReadingAction.ToggleVisibility);

    [RelayCommand(CanExecute = nameof(CanRecognize))]
    private Task RecognizeAsync() => ExecuteAsync(ReadingAction.Recognize);
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Hide() => _ = ExecuteAsync(ReadingAction.Stop);
    [RelayCommand(CanExecute = nameof(CanVisibility))]
    private Task ToggleVisibilityAsync() => ExecuteAsync(ReadingAction.ToggleVisibility);
    [RelayCommand(CanExecute = nameof(CanRegion))]
    private void SetRecognitionRegion() => _ = ExecuteAsync(ReadingAction.SelectRegion);

    private async Task ExecuteAsync(ReadingAction action)
    {
        if (_disposed) return;
        _support.CancelStartupReminder();
        try { await Actions.ExecuteAsync(action); }
        catch (Exception exception) { SetError(exception, action == ReadingAction.SelectRegion ? ErrorCodes.TargetWindowUnavailable : ErrorCodes.RecognitionFailed); }
    }

    private async Task SetAutomaticAsync(bool enabled)
    {
        try
        {
            if (!Actions.CanExecute(ReadingAction.ToggleAutomatic)) return;
            _support.CancelStartupReminder();
            await Actions.SetAutomaticAsync(enabled);
        }
        catch (Exception exception) { SetError(exception, ErrorCodes.RecognitionFailed); }
        finally { OnPropertyChanged(nameof(AutoRecognitionEnabled)); }
    }

    private void OnSelectionCommitted(object? sender, EventArgs e) => _session.Stop();
    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Selection.IsSelectingWindow) _support.CancelStartupReminder();
        if (e.PropertyName == nameof(TargetSelectionViewModel.Status))
        {
            _renderStatus = () => Selection.Status;
            Status = _renderStatus();
        }
        NotifyCommands();
    }

    private void OnRegionChanged(object? sender, EventArgs e)
    {
        Selection.SetRegionSelectionActive(_region.IsSelecting);
        NotifyCommands();
    }

    private void OnNotice(object? sender, string key) => SetStatus(key);
    private void OnActionFailed(object? sender, ReadingFailure failure)
        => SetError(failure.Exception, failure.Action == ReadingAction.SelectRegion ? ErrorCodes.TargetWindowUnavailable : ErrorCodes.RecognitionFailed);
    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        var state = _session.State;
        if (state.Phase != _previousPhase)
        {
            if (state.Phase is RecognitionPhase.Recognizing or RecognitionPhase.Restoring)
            {
                Selection.BeginRecognition();
                _region.StopFlashing();
            }
            if (state.Phase is RecognitionPhase.Hidden or RecognitionPhase.Stopped or RecognitionPhase.Failed)
            {
                _region.StopFlashing();
                if (!Selection.IsAcquiringPreview) _capture.Stop();
                if (state.Phase != RecognitionPhase.Hidden) Selection.EndRecognition();
            }
        }
        _previousPhase = state.Phase;
        if (state.Error is not null)
        {
            if (!ReferenceEquals(_shownError, state.Error)) SetError(state.Error, ErrorCodes.RecognitionFailed);
        }
        else switch (state.Phase)
            {
                case RecognitionPhase.Recognizing:
                case RecognitionPhase.Restoring: SetStatus(ResourceKeys.Status_Recognizing); break;
                case RecognitionPhase.Ready: SetStatus(state.WordCount == 0 ? ResourceKeys.Status_NoWords : ResourceKeys.Status_WordsRecognized, state.WordCount); break;
                case RecognitionPhase.Hidden: SetStatus("Reading_HiddenPaused"); break;
                case RecognitionPhase.Stopped: SetStatus(ResourceKeys.Status_Hidden); break;
            }
        OnPropertyChanged(nameof(AutoRecognitionEnabled));
        OnPropertyChanged(nameof(IsHidden));
        OnPropertyChanged(nameof(VisibilityButtonText));
        OnPropertyChanged(nameof(RunState));
        NotifyCommands();
    }

    private void OnCompleted(object? sender, RecognitionCompleted completion)
    {
        if (_disposed) return;
        _support.RecordRecognition(DateTime.Now);
        if (!completion.IsAutomatic && !completion.Result.Lines.Any(line => line.Words.Count > 0))
        {
            var saved = RecognitionRegion.TryParse(_settings.GetValue(RecognitionRegion.SettingsKey), out var parsed) ? parsed : null;
            if (Selection.SelectedWindow is { } target) _region.Flash(target, saved);
        }
        else if (completion.Result.Lines.Any(line => line.Words.Count > 0)) _region.StopFlashing();
    }

    private void NotifyCommands()
    {
        RecognizeCommand.NotifyCanExecuteChanged();
        SetRecognitionRegionCommand.NotifyCanExecuteChanged();
        HideCommand.NotifyCanExecuteChanged();
        ToggleVisibilityCommand.NotifyCanExecuteChanged();
    }

    private void SetStatus(string key, params object[] args)
    {
        _shownError = null;
        _renderStatus = () => _localizer.Get(key, args);
        Status = _renderStatus();
    }

    private void SetError(Exception exception, string fallback)
    {
        _shownError = exception;
        _logger.LogError(exception, "Error reported to user");
        _renderStatus = () => _messages.Resolve(exception, fallback);
        Status = _renderStatus();
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        Status = _renderStatus();
        OnPropertyChanged(nameof(VisibilityButtonText));
        OnPropertyChanged(nameof(RunState));
        NotifyCommands();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _session.Stop();
        _disposed = true;
        _session.Changed -= OnSessionChanged;
        _session.Completed -= OnCompleted;
        Actions.Notice -= OnNotice;
        Actions.Failed -= OnActionFailed;
        Selection.SelectionCommitted -= OnSelectionCommitted;
        Selection.PropertyChanged -= OnSelectionPropertyChanged;
        _region.SelectionStateChanged -= OnRegionChanged;
        _localizer.CultureChanged -= OnCultureChanged;
        Selection.Dispose();
        _session.Dispose();
    }
}

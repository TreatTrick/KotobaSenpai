using KotobaSenpai.Core.Workflow;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Errors;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.ViewModels;

/// <summary>
/// Recognition page view model: orchestrates the window selection, recognition, and hide use cases.
/// Depends only on Core ports and application services (including the localization ports <see cref="IStringLocalizer"/>
/// and <see cref="IUserMessageResolver"/>), and references no WPF or platform implementation, so it can be tested headlessly.
/// All user-visible text is resolved through <see cref="IStringLocalizer"/>; on culture switch <see cref="Status"/> is recomputed from the current state.
/// </summary>
public sealed partial class RecognitionPageViewModel : ObservableObject, IDisposable
{
    private readonly IWindowCatalog _catalog;
    private readonly WordOverlayApplicationService _workflow;
    private readonly IRegionSelector _regionSelector;
    private readonly ISettingsService _settings;
    private readonly IStringLocalizer _localizer;
    private readonly IUserMessageResolver _messageResolver;
    private readonly ILogger _logger;
    private readonly ITargetWindowTracker? _tracker;
    private readonly IAutoRecognitionController? _autoRecognitionController;
    private readonly IWindowFrameCapture? _capture;
    private bool _recognitionSessionActive;
    private bool _lastRecognitionWasEmpty;
    private CancellationTokenSource? _manualRecognition;
    private int _sessionVersion;
    private bool _disposed;

    /// <summary>A delegate that renders the localized Status from the current state; re-invoked on culture switch to refresh in place.</summary>
    private Func<string> _renderStatus = () => string.Empty;

    public RecognitionPageViewModel(
        IWindowCatalog catalog,
        WordOverlayApplicationService workflow,
        IRegionSelector regionSelector,
        ISettingsService settings,
        IStringLocalizer localizer,
        IUserMessageResolver messageResolver,
        ILogger logger,
        ITargetWindowTracker? tracker = null,
        IAutoRecognitionController? autoRecognitionController = null,
        IWindowFrameCapture? capture = null)
    {
        _catalog = catalog;
        _workflow = workflow;
        _regionSelector = regionSelector;
        _settings = settings;
        _localizer = localizer;
        _messageResolver = messageResolver;
        _logger = logger;
        _tracker = tracker;
        _autoRecognitionController = autoRecognitionController;
        _capture = capture;
        _autoRecognitionEnabled = IsEnabled(_settings.GetValue(AutoRecognitionSettings.EnabledKey));

        _localizer.CultureChanged += OnCultureChanged;
        SetStatus(ResourceKeys.Status_SelectTarget);
    }

    /// <summary>List of selectable target windows.</summary>
    public ObservableCollection<WindowTarget> Windows { get; } = new();

    /// <summary>The currently selected target window.</summary>
    [ObservableProperty]
    private WindowTarget? _selectedWindow;

    /// <summary>Whether polling recognition is enabled for the current recognition session.</summary>
    [ObservableProperty]
    private bool _autoRecognitionEnabled;

    /// <summary>User-visible status text.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// The main window's own handle, used to exclude itself from the candidate list.
    /// Set by the view after the window handle is created (HWND is a platform detail that should not leak into the domain).
    /// </summary>
    public nint ExcludeHandle { get; set; }

    /// <summary>Updates the status prompt after a window is selected.</summary>
    partial void OnSelectedWindowChanged(WindowTarget? value)
    {
        EndRecognitionSession();
        try
        {
            if (value is null)
                _tracker?.Detach();
            else
                _tracker?.Attach(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attach the selected target window");
        }

        SetStatus(value is null ? ResourceKeys.Status_SelectTarget : ResourceKeys.Status_Selected,
                  value?.Title ?? string.Empty);
    }

    /// <summary>Re-enumerates visible windows and refreshes the candidate list.</summary>
    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var previousHandle = SelectedWindow?.Handle;
            var current = _catalog.ListVisibleWindows()
                .Where(window => window.Handle != ExcludeHandle)
                .OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            Windows.Clear();
            foreach (var window in current) Windows.Add(window);
            SelectedWindow = Windows.FirstOrDefault(window => window.Handle == previousHandle)
                ?? Windows.FirstOrDefault();
            SetStatus(Windows.Count == 0 ? ResourceKeys.Status_NoWindows : ResourceKeys.Status_WindowsFound,
                      Windows.Count);
        }
        catch (Exception ex)
        {
            SetError(ex, ErrorCodes.WindowEnumerationFailed);
        }
    }

    /// <summary>Captures and recognizes Japanese words in the current window, drawing underlines.</summary>
    [RelayCommand]
    private async Task RecognizeAsync()
    {
        if (SelectedWindow is null)
        {
            SetStatus(ResourceKeys.Status_SelectTargetFirst);
            return;
        }

        var target = SelectedWindow;
        var version = ++_sessionVersion;
        using var cancellation = new CancellationTokenSource();
        _manualRecognition = cancellation;
        try
        {
            _autoRecognitionController?.Stop();
            _recognitionSessionActive = false;
            _lastRecognitionWasEmpty = false;
            _regionSelector.StopFlashing();
            SetStatus(ResourceKeys.Status_Recognizing);
            var result = await _workflow.RecognizeAndShowAsync(target, cancellation.Token);
            if (cancellation.IsCancellationRequested || version != _sessionVersion) return;
            _recognitionSessionActive = true;
            var wordCount = result.Lines.Sum(line => line.Words.Count);
            UpdateRegionFeedback(target, wordCount == 0);
            SetStatus(wordCount == 0
                ? ResourceKeys.Status_NoWords
                : ResourceKeys.Status_WordsRecognized,
                wordCount);
            StartAutoRecognitionIfEnabled();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || version != _sessionVersion)
        {
        }
        catch (Exception ex)
        {
            if (version != _sessionVersion) return;
            EndRecognitionSession();
            SetError(ex, ErrorCodes.RecognitionFailed);
        }
        finally
        {
            if (ReferenceEquals(_manualRecognition, cancellation)) _manualRecognition = null;
        }
    }

    /// <summary>Hides the underline overlay.</summary>
    [RelayCommand]
    private void Hide()
    {
        EndRecognitionSession();
        SetStatus(ResourceKeys.Status_Hidden);
    }

    private void EndRecognitionSession()
    {
        _sessionVersion++;
        _manualRecognition?.Cancel();
        _autoRecognitionController?.Stop();
        _recognitionSessionActive = false;
        _lastRecognitionWasEmpty = false;
        _regionSelector.StopFlashing();
        _capture?.Stop();
        _workflow.Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
        EndRecognitionSession();
    }

    partial void OnAutoRecognitionEnabledChanged(bool value)
    {
        _settings.SetValue(AutoRecognitionSettings.EnabledKey, value ? "true" : "false");
        if (value)
            StartAutoRecognitionIfEnabled();
        else
            _autoRecognitionController?.Stop();
    }

    private void StartAutoRecognitionIfEnabled()
    {
        if (!_recognitionSessionActive || !AutoRecognitionEnabled || SelectedWindow is null || _autoRecognitionController is null)
            return;

        var target = SelectedWindow;
        _autoRecognitionController.Start(target, cancellationToken => RecognizeChangedAsync(target, cancellationToken));
    }

    private async Task RecognizeChangedAsync(WindowTarget target, CancellationToken cancellationToken)
    {
        var version = _sessionVersion;
        try
        {
            var result = await _workflow.RecognizeChangedAndShowAsync(target, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is not null && version == _sessionVersion && _recognitionSessionActive)
                UpdateRegionFeedback(target, result.Lines.All(line => line.Words.Count == 0));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                _logger.LogError(ex, "Automatic recognition failed.");
        }
    }

    private static bool IsEnabled(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private void UpdateRegionFeedback(WindowTarget target, bool isEmpty)
    {
        if (isEmpty && !_lastRecognitionWasEmpty)
            _regionSelector.Flash(target, ReadSavedRegion());
        else if (!isEmpty)
            _regionSelector.StopFlashing();
        _lastRecognitionWasEmpty = isEmpty;
    }

    /// <summary>Opens the interactive region selector over the selected window to set the recognition sub-region.</summary>
    [RelayCommand]
    private void SetRecognitionRegion()
    {
        if (SelectedWindow is null)
        {
            SetStatus(ResourceKeys.Status_SelectTargetFirst);
            return;
        }
        try
        {
            _tracker?.Refresh();
            _regionSelector.Show(SelectedWindow, ReadSavedRegion());
            SetStatus(ResourceKeys.Status_RegionSelecting);
        }
        catch (Exception ex)
        {
            SetError(ex, ErrorCodes.TargetWindowUnavailable);
        }
    }

    private RecognitionRegion? ReadSavedRegion()
    {
        var raw = _settings.GetValue(RecognitionRegion.SettingsKey);
        return RecognitionRegion.TryParse(raw, out var region) ? region : null;
    }

    /// <summary>Records a normal status (key + format arguments) and renders it.</summary>
    private void SetStatus(string key, params object[] args)
    {
        _renderStatus = () => _localizer.Get(key, args);
        Status = _renderStatus();
    }

    /// <summary>Records an error status (exception + fallback code) and renders it through the resolver; re-translated by code on culture switch.</summary>
    private void SetError(Exception exception, string fallbackErrorCode)
    {
        _logger.LogError(exception, "Error reported to user");
        _renderStatus = () => _messageResolver.Resolve(exception, fallbackErrorCode);
        Status = _renderStatus();
    }

    /// <summary>On culture switch, re-derives <see cref="Status"/> from the current state and notifies the view.</summary>
    private void OnCultureChanged(object? sender, EventArgs e)
        => Status = _renderStatus();
}

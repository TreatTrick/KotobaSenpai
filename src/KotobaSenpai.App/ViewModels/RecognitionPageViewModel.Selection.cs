using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Geometry;
using KotobaSenpai.App.Resources;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Errors;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.ViewModels;

public sealed partial class RecognitionPageViewModel
{
    private readonly IWindowPicker _picker;
    private readonly SynchronizationContext? _selectionContext = SynchronizationContext.Current;
    private readonly int _selectionThread = Environment.CurrentManagedThreadId;
    private CancellationTokenSource? _selectionCancellation;
    private bool _committingSelection;
    private bool _acquiringPreview;
    private bool _targetUnavailable;
    private bool _targetClosed;
    private bool _awaitingRecognition;
    private Exception? _previewError;

    public event EventHandler<WindowPreviewEventArgs>? PreviewReady;
    public long SelectionVersion { get; private set; }
    [ObservableProperty] private bool _isSelectingWindow;
    [ObservableProperty] private bool _isPickingWindow;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _hasPreviewError;

    public string TargetTitle => SelectedWindow is null ? _localizer.Get(ResourceKeys.WindowPicker_Empty)
        : string.IsNullOrWhiteSpace(SelectedWindow.Title) ? _localizer.Get(ResourceKeys.WindowPicker_Untitled) : SelectedWindow.Title;

    public string SelectionButtonText => _localizer.Get(SelectedWindow is null ? ResourceKeys.WindowPicker_Select : ResourceKeys.WindowPicker_Reselect);

    public string TargetStatus => _localizer.Get(_targetClosed ? ResourceKeys.WindowPicker_Closed
        : IsPickingWindow ? ResourceKeys.WindowPicker_Picking
        : _targetUnavailable ? ResourceKeys.WindowPicker_Unavailable
        : _acquiringPreview ? ResourceKeys.WindowPicker_Loading
        : HasPreviewError ? ResourceKeys.WindowPicker_PreviewFailed
        : SelectedWindow is null ? ResourceKeys.WindowPicker_EmptyHint : ResourceKeys.WindowPicker_Selected);

    public string PreviewError => _previewError is null ? string.Empty : _messageResolver.Resolve(_previewError, ErrorCodes.WindowCaptureFailed);

    private bool CanOperate() => !_disposed && !IsSelectingWindow;
    private bool CanRetryPreview() => CanOperate() && HasPreviewError && SelectedWindow is not null
        && !_targetUnavailable && !_recognitionSessionActive && _manualRecognition is null;

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private async Task SelectWindowAsync()
    {
        if (!CanOperate()) return;
        using var cancellation = new CancellationTokenSource();
        _selectionCancellation = cancellation;
        IsSelectingWindow = true;
        try
        {
            WindowTarget? target;
            IsPickingWindow = true;
            try { target = await _picker.PickAsync(cancellation.Token); }
            finally { IsPickingWindow = false; }
            if (target is null || cancellation.IsCancellationRequested || _disposed) return;
            _committingSelection = true;
            try
            {
                if (Equals(SelectedWindow, target)) OnSelectedWindowChanged(target);
                else SelectedWindow = target;
            }
            finally { _committingSelection = false; }
            if (SelectedWindow is not null && !cancellation.IsCancellationRequested)
                await CapturePreviewAsync(animate: true, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || _disposed) { }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested && !_disposed) SetError(exception, ErrorCodes.WindowSelectionFailed);
        }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, cancellation)) _selectionCancellation = null;
            IsPickingWindow = false;
            IsSelectingWindow = false;
            NotifySelectionState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetryPreview))]
    private async Task RetryPreviewAsync()
    {
        if (!CanRetryPreview()) return;
        using var cancellation = new CancellationTokenSource();
        _selectionCancellation = cancellation;
        IsSelectingWindow = true;
        try { await CapturePreviewAsync(animate: false, cancellation.Token); }
        finally
        {
            if (ReferenceEquals(_selectionCancellation, cancellation)) _selectionCancellation = null;
            IsSelectingWindow = false;
            NotifySelectionState();
        }
    }

    private async Task CapturePreviewAsync(bool animate, CancellationToken cancellationToken)
    {
        var target = SelectedWindow;
        var version = SelectionVersion;
        if (target is null || _capture is null) return;
        _acquiringPreview = true;
        HasPreviewError = false;
        _previewError = null;
        NotifySelectionState();
        try
        {
            var before = _tracker?.Refresh();
            if (before is not null && (before.Handle != target.Handle || !before.IsCapturable))
                throw new InvalidOperationException("The selected target is unavailable for preview.");
            var frame = await _capture.CaptureAsync(before?.Target ?? target, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || version != SelectionVersion) return;
            var after = _tracker?.Refresh();
            if (after is not null && (after.Handle != target.Handle || !after.IsCapturable
                || after.Bounds.Width != frame.Width || after.Bounds.Height != frame.Height))
                throw new InvalidOperationException("The target layout changed during preview acquisition.");
            PreviewReady?.Invoke(this, new WindowPreviewEventArgs(version, after?.Bounds ?? target.Bounds, frame, animate));
            if (_disposed || version != SelectionVersion) return;
            HasPreview = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed || version != SelectionVersion)
        {
            if (!_disposed && version == SelectionVersion && SelectedWindow is not null) HasPreviewError = true;
        }
        catch (Exception exception)
        {
            if (!_disposed && !cancellationToken.IsCancellationRequested && version == SelectionVersion)
            {
                HasPreview = false;
                HasPreviewError = true;
                _previewError = exception;
                _logger.LogError(exception, "Failed to capture the selected window preview.");
            }
        }
        finally
        {
            // New recognition is disabled until this owner has stopped its preview-only capture.
            _capture.Stop();
            _acquiringPreview = false;
            NotifySelectionState();
        }
    }

    /// <summary>Cancels picking or preview work without changing an already committed selection.</summary>
    public void CancelWindowSelection() => _selectionCancellation?.Cancel();

    internal void ReportPresentationFailure(Exception exception)
        => _logger.LogError(exception, "Failed to present the window selection animation.");

    private void InvalidatePreview()
    {
        SelectionVersion++;
        HasPreview = false;
        HasPreviewError = false;
        _previewError = null;
        _targetClosed = false;
        _targetUnavailable = false;
        OnPropertyChanged(nameof(SelectionVersion));
        NotifySelectionState();
    }

    private void OnTargetStateChanged(object? sender, TargetWindowSnapshot snapshot)
    {
        var version = SelectionVersion;
        if (_selectionContext is not null && Environment.CurrentManagedThreadId != _selectionThread)
        {
            _selectionContext.Post(_ => { if (version == SelectionVersion) ApplyTargetState(snapshot); }, null);
            return;
        }
        ApplyTargetState(snapshot);
    }

    private void ApplyTargetState(TargetWindowSnapshot snapshot)
    {
        if (_disposed || SelectedWindow?.Handle != snapshot.Handle) return;
        if (!snapshot.IsValid)
        {
            // Closing the old target must not cancel an independent desktop picking session.
            var committing = _committingSelection;
            _committingSelection = IsPickingWindow;
            try { SelectedWindow = null; }
            finally { _committingSelection = committing; }
            _targetClosed = true;
        }
        else
        {
            _targetUnavailable = !snapshot.IsCapturable;
            if (_targetUnavailable && _acquiringPreview)
            {
                HasPreviewError = true;
                _selectionCancellation?.Cancel();
            }
        }
        UpdateSelectionHighlight();
        NotifySelectionState();
    }

    private void UpdateSelectionHighlight()
    {
        try
        {
            _picker.Highlight(_awaitingRecognition && !_disposed && !IsPickingWindow
                ? SelectedWindow : null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update the selected window outline.");
        }
    }

    private void NotifySelectionState()
    {
        OnPropertyChanged(nameof(TargetTitle));
        OnPropertyChanged(nameof(TargetStatus));
        OnPropertyChanged(nameof(PreviewError));
        OnPropertyChanged(nameof(SelectionButtonText));
        SelectWindowCommand.NotifyCanExecuteChanged();
        RetryPreviewCommand.NotifyCanExecuteChanged();
        RecognizeCommand.NotifyCanExecuteChanged();
        SetRecognitionRegionCommand.NotifyCanExecuteChanged();
        HideCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSelectingWindowChanged(bool value) => NotifySelectionState();
    partial void OnIsPickingWindowChanged(bool value)
    {
        UpdateSelectionHighlight();
        NotifySelectionState();
    }
}

/// <summary>An owned frame handed directly to the view, never retained by the view model.</summary>
public sealed class WindowPreviewEventArgs(long version, ScreenRect bounds, CapturedFrame frame, bool animate) : EventArgs
{
    public long Version { get; } = version;
    public ScreenRect Bounds { get; } = bounds;
    public CapturedFrame Frame { get; } = frame;
    public bool Animate { get; } = animate;
}

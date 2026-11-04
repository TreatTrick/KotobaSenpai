using KotobaSenpai.App.ViewModels;
using KotobaSenpai.App.Support;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.TargetWindow;
using KotobaSenpai.Core.Workflow;

namespace KotobaSenpai.App.Reading;

public enum ReadingAction { Recognize, ToggleAutomatic, Stop, ToggleVisibility, SelectRegion }
public sealed record ReadingFailure(ReadingAction Action, Exception Exception);

public interface IReadingActions
{
    bool CanExecute(ReadingAction action);
    Task ExecuteAsync(ReadingAction action);
}

/// <summary>Shares interaction checks between buttons and global keyboard actions.</summary>
public sealed class ReadingActions(
        TargetSelectionViewModel selection,
        RecognitionSessionController session,
        IRegionSelector region, ISettingsService settings,
        ITargetWindowTracker? tracker = null,
        SupportService? support = null) : IReadingActions
{
    public Func<bool> IsInteractionEnabled { get; set; } = () => true;
    public event EventHandler<string>? Notice;
    public event EventHandler<ReadingFailure>? Failed;

    public bool CanExecute(ReadingAction action)
        => IsInteractionEnabled() && !selection.IsSelectingWindow
            && (!region.IsSelecting || action == ReadingAction.Stop)
            && (action != ReadingAction.Recognize || !session.IsBusy);

    public async Task ExecuteAsync(ReadingAction action)
    {
        if (!CanExecute(action)) return;
        support?.CancelStartupReminder();
        try { await ExecuteCoreAsync(action); }
        catch (Exception exception) { Failed?.Invoke(this, new(action, exception)); }
    }

    public async Task SetAutomaticAsync(bool enabled)
    {
        if (!CanExecute(ReadingAction.ToggleAutomatic)) return;
        support?.CancelStartupReminder();
        try
        {
            await session.SetAutoRecognitionAsync(enabled, selection.IsTargetReady ? selection.SelectedWindow : null);
            if (selection.SelectedWindow is null) Notice?.Invoke(this, "Status_SelectTargetFirst");
            else if (!selection.IsTargetReady) Notice?.Invoke(this, "WindowPicker_Unavailable");
        }
        catch (Exception exception) { Failed?.Invoke(this, new(ReadingAction.ToggleAutomatic, exception)); }
    }

    private async Task ExecuteCoreAsync(ReadingAction action)
    {
        var target = selection.SelectedWindow;
        switch (action)
        {
            case ReadingAction.Stop:
                region.Cancel();
                session.Stop();
                break;
            case ReadingAction.ToggleAutomatic:
                await SetAutomaticAsync(!session.State.AutoRecognitionEnabled);
                break;
            case ReadingAction.ToggleVisibility when session.State.Phase != RecognitionPhase.Hidden:
                if (!session.HasSession) Notice?.Invoke(this, "Reading_StartFirst");
                else session.PauseAndHide();
                break;
            case ReadingAction.Recognize when session.State.Phase == RecognitionPhase.Hidden:
                Notice?.Invoke(this, "Reading_ResumeFirst");
                break;
            default:
                if (target is null)
                {
                    Notice?.Invoke(this, "Status_SelectTargetFirst");
                    return;
                }
                if (!selection.IsTargetReady)
                {
                    Notice?.Invoke(this, "WindowPicker_Unavailable");
                    return;
                }
                if (action == ReadingAction.Recognize) await session.RecognizeAsync(target);
                else if (action == ReadingAction.ToggleVisibility) await session.ResumeAsync(target);
                else if (action == ReadingAction.SelectRegion)
                {
                    if (tracker?.Refresh() is { IsRenderable: false })
                    {
                        Notice?.Invoke(this, "WindowPicker_Unavailable");
                        return;
                    }
                    var saved = RecognitionRegion.TryParse(settings.GetValue(RecognitionRegion.SettingsKey), out var parsed) ? parsed : null;
                    region.Show(target, saved);
                    Notice?.Invoke(this, "Status_RegionSelecting");
                }
                break;
        }
    }
}

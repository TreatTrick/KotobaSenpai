using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.App.Reading;
using KotobaSenpai.App.Shortcuts;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Shortcuts;

namespace KotobaSenpai.App.ViewModels;

/// <summary>Projects the same binding snapshot into settings rows and action hints.</summary>
public sealed class ShortcutSettingsViewModel : IDisposable
{
    private readonly ReadingShortcutCoordinator _coordinator;
    private readonly IStringLocalizer _localizer;
    public IReadOnlyList<ShortcutEntryViewModel> Items { get; }
    public ShortcutEntryViewModel Recognize => Items[0];
    public ShortcutEntryViewModel Automatic => Items[1];
    public ShortcutEntryViewModel Stop => Items[2];
    public ShortcutEntryViewModel Visibility => Items[3];
    public ShortcutEntryViewModel Region => Items[4];

    public ShortcutSettingsViewModel(ReadingShortcutCoordinator coordinator, IStringLocalizer localizer)
    {
        _coordinator = coordinator;
        _localizer = localizer;
        Items = Enum.GetValues<ReadingAction>().Select(action => new ShortcutEntryViewModel(coordinator, localizer, action)).ToArray();
        coordinator.Changed += OnChanged;
        localizer.CultureChanged += OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        foreach (var item in Items) item.Refresh();
    }

    public void Dispose()
    {
        _coordinator.Changed -= OnChanged;
        _localizer.CultureChanged -= OnChanged;
    }
}

public sealed record ShortcutKey(string Text, bool HasSeparator);

public sealed partial class ShortcutEntryViewModel(ReadingShortcutCoordinator coordinator,
    IStringLocalizer localizer, ReadingAction action) : ObservableObject
{
    private ShortcutEditResult? _editResult;
    public ReadingAction Action { get; } = action;
    public string Name => localizer.Get("Shortcut_Action_" + Action);
    public string GestureText => coordinator.GetBinding(Action).Gesture?.ToString() ?? "";
    public string ShortcutLabel => localizer.Get("Shortcut_Label");
    public IReadOnlyList<ShortcutKey> Keys => GestureText.Split('+', StringSplitOptions.RemoveEmptyEntries)
        .Select((key, index) => new ShortcutKey(key, index > 0)).ToArray();
    public string InactiveText => GestureText.Length > 0 && coordinator.GetBinding(Action).Error != ShortcutBindingError.None
        ? localizer.Get("Shortcut_Inactive") : "";
    public bool IsRecording => coordinator.RecordingAction == Action;
    public string CaptureText => IsRecording ? localizer.Get("Shortcut_PressKey")
        : GestureText.Length > 0 ? GestureText : localizer.Get("Shortcut_Unbound");
    public string Hint => GestureText.Length == 0 ? "" : GestureText
        + (coordinator.GetBinding(Action).Error == ShortcutBindingError.None ? "" : " · " + localizer.Get("Shortcut_Inactive"));
    public string HelpText => Name + (Hint.Length == 0 ? "" : " — " + Hint);
    public string Status
    {
        get
        {
            var binding = coordinator.GetBinding(Action);
            var editFailure = _editResult is { Succeeded: false } ? _editResult : null;
            var error = editFailure?.Error ?? binding.Error;
            if (error != ShortcutBindingError.None)
            {
                var other = editFailure?.Conflict ?? Enum.GetValues<ReadingAction>()
                    .Where(value => value != Action && binding.Gesture is not null && coordinator.GetBinding(value).Gesture == binding.Gesture)
                    .Select(value => (ReadingAction?)value).FirstOrDefault();
                var conflict = other is not null ? localizer.Get("Shortcut_Action_" + other) : "";
                return localizer.Get("Shortcut_Error_" + error, conflict, editFailure?.NativeError ?? binding.NativeError);
            }
            return IsRecording ? localizer.Get("Shortcut_RecordingHelp")
                : binding.Gesture is null ? localizer.Get("Shortcut_Unbound") : localizer.Get("Shortcut_Active");
        }
    }

    public bool BeginRecording()
    {
        _editResult = null;
        return coordinator.BeginRecording(Action);
    }

    public void FinishRecording(ShortcutGesture? gesture, bool invalid = false)
    {
        coordinator.EndRecording();
        if (invalid) _editResult = new(ShortcutBindingError.Invalid);
        else if (gesture is not null) _editResult = coordinator.ChangeBinding(Action, gesture);
        Refresh();
    }

    [RelayCommand] private void Clear() => Change(null);
    [RelayCommand] private void Reset() => Change(ReadingShortcutCoordinator.DefaultGesture(Action));
    private void Change(ShortcutGesture? gesture)
    {
        _editResult = coordinator.ChangeBinding(Action, gesture);
        Refresh();
    }

    public void Refresh()
    {
        foreach (var property in new[] { nameof(Name), nameof(GestureText), nameof(ShortcutLabel), nameof(Keys), nameof(InactiveText), nameof(IsRecording), nameof(CaptureText), nameof(Hint), nameof(HelpText), nameof(Status) })
            OnPropertyChanged(property);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KotobaSenpai.Core.Localization;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Support;

public sealed record SupportState
{
    public int Version { get; init; } = 1;
    public long RecognitionCount { get; init; }
    public DateOnly[] ActiveDays { get; init; } = [];
    public DateOnly? LastPromptDate { get; init; }
    public int LastPromptMilestone { get; init; }
    public bool HasSupported { get; init; }
    public bool HasTipped { get; init; }
    [JsonIgnore]
    public int PendingMilestone => HasSupported || HasTipped ? 0
        : ActiveDays.Length >= 7 && LastPromptMilestone < 7 ? 7
        : ActiveDays.Length >= 3 && LastPromptMilestone < 3 ? 3 : 0;
}

/// <summary>Owns the single persisted support state; recognition never schedules a popup.</summary>
public sealed partial class SupportService : ObservableObject, IDisposable
{
    public const string SettingsKey = "SupportState";
    public const string GitHubLink = "https://github.com/TreatTrick/KotobaSenpai";
    public const string PayPalLink = "https://www.paypal.com/ncp/payment/GVHHQGRMT9YYJ";
    private readonly ISettingsService _settings;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger _logger;
    private readonly int _startupMilestone;
    private bool _startupCancelled;
    private bool _startupChecked;
    private bool _disposed;
    private string? _errorKey;

    public SupportService(ISettingsService settings, IStringLocalizer localizer, ILogger logger)
    {
        _settings = settings;
        _localizer = localizer;
        _logger = logger;
        try
        {
            var raw = settings.GetValue(SettingsKey);
            var restored = raw is null ? null : JsonSerializer.Deserialize<SupportState>(raw);
            if (restored is { Version: 1, RecognitionCount: >= 0, ActiveDays: not null }
                && restored.LastPromptMilestone is 0 or 3 or 7)
                State = restored with { ActiveDays = restored.ActiveDays.Distinct().Order().TakeLast(30).ToArray() };
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to restore support state."); }
        _startupMilestone = State.PendingMilestone;
        _localizer.CultureChanged += OnCultureChanged;
    }

    public SupportState State { get; private set; } = new();
    public bool IsSupported => State.HasTipped;
    public string EntryText => _localizer.Get(IsSupported ? "support.already" : "support.button");
    public string EntryHelp => _localizer.Get(IsSupported ? "support.supported.help" : "support.button")
#if DEBUG
        + "\n" + _localizer.Get("support.debug.reset.help")
#endif
        ;
    public string Error => _errorKey is null ? string.Empty : _localizer.Get(_errorKey);
    public event EventHandler? OpenRequested;
    public event EventHandler? MainViewReady;
    public void NotifyMainViewReady() => MainViewReady?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public void RecordRecognition(DateTime completedAt)
    {
        if (_disposed) return;
        var date = DateOnly.FromDateTime(completedAt);
        Save(State with
        {
            RecognitionCount = State.RecognitionCount + 1,
            ActiveDays = State.ActiveDays.Append(date).Distinct().Order().TakeLast(30).ToArray(),
        });
    }

    public void CancelStartupReminder() => _startupCancelled = true;

    public bool TryMarkStartupPrompt(DateTime now, bool canShow)
    {
        if (_disposed || _startupChecked) return false;
        _startupChecked = true;
        if (_startupCancelled || !canShow || _startupMilestone == 0 || State.HasSupported || State.HasTipped
            || State.LastPromptMilestone >= _startupMilestone || now.Hour is < 10 or >= 19
            || State.LastPromptDate == DateOnly.FromDateTime(now)) return false;
        return Save(State with { LastPromptMilestone = _startupMilestone, LastPromptDate = DateOnly.FromDateTime(now) });
    }

    public bool ConfirmSupport() => Save(State with { HasSupported = true, HasTipped = true });
    public bool RecordGitHubVisit() => Save(State with { HasSupported = true });

#if DEBUG
    public bool ResetForDebugging()
    {
        if (!Save(State with { HasSupported = false, HasTipped = false, LastPromptDate = null, LastPromptMilestone = 0 }))
            return false;
        CancelStartupReminder();
        return true;
    }
#endif

    private bool Save(SupportState next)
    {
        if (_disposed) return false;
        try
        {
            _settings.SetValue(SettingsKey, JsonSerializer.Serialize(next));
            State = next;
            _errorKey = null;
            NotifyPresentation();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save support state.");
            _errorKey = "support.save.failed";
            OnPropertyChanged(nameof(Error));
            return false;
        }
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(EntryText));
        OnPropertyChanged(nameof(EntryHelp));
        OnPropertyChanged(nameof(Error));
    }

    private void OnCultureChanged(object? sender, EventArgs e) => NotifyPresentation();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelStartupReminder();
        _localizer.CultureChanged -= OnCultureChanged;
    }
}

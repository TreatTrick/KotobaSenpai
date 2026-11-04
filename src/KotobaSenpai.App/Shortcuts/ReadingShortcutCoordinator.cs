using KotobaSenpai.App.Reading;
using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;
using KotobaSenpai.Core.Shortcuts;

namespace KotobaSenpai.App.Shortcuts;

public enum ShortcutBindingError { None, Invalid, Duplicate, Unavailable, SaveFailed }
public sealed record ShortcutBinding(ReadingAction Action, ShortcutGesture? Gesture,
    int? RegistrationId = null, ShortcutBindingError Error = ShortcutBindingError.None, int NativeError = 0);
public sealed record ShortcutEditResult(ShortcutBindingError Error = ShortcutBindingError.None,
    ReadingAction? Conflict = null, int NativeError = 0)
{
    public bool Succeeded => Error == ShortcutBindingError.None;
}

/// <summary>Owns committed keyboard bindings and connects native IDs to the shared reading actions.</summary>
public sealed class ReadingShortcutCoordinator : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IGlobalShortcutRegistrar _registrar;
    private readonly ILogger? _logger;
    private readonly Dictionary<ReadingAction, ShortcutBinding> _bindings = [];
    private IReadingActions? _actions;
    private int _nextId;
    private bool _disposed;
    private bool _started;

    public ReadingShortcutCoordinator(ISettingsService settings, IGlobalShortcutRegistrar registrar, ILogger? logger = null)
    {
        _settings = settings;
        _registrar = registrar;
        _logger = logger;
        foreach (var action in Enum.GetValues<ReadingAction>())
        {
            var raw = settings.GetValue(SettingsKey(action));
            var gesture = raw is null ? DefaultGesture(action)
                : ShortcutGesture.TryParse(raw, out var parsed) ? parsed : (ShortcutGesture?)null;
            var error = raw is { Length: > 0 } && gesture is null ? ShortcutBindingError.Invalid
                : gesture is not null && _bindings.Values.Any(binding => binding.Gesture == gesture)
                    ? ShortcutBindingError.Duplicate : ShortcutBindingError.None;
            _bindings[action] = new(action, gesture, Error: error);
        }
        registrar.Triggered += OnTriggered;
    }

    public event EventHandler? Changed;
    public ReadingAction? RecordingAction { get; private set; }
    public bool IsRecording => RecordingAction is not null;
    public static string SettingsKey(ReadingAction action) => "ReadingShortcut." + action;
    public static ShortcutGesture DefaultGesture(ReadingAction action) => new("F" + ((int)action + 1));
    public ShortcutBinding GetBinding(ReadingAction action) => _bindings[action];
    public void Attach(IReadingActions actions) => _actions = actions;
    public void Detach() => _actions = null;

    /// <summary>Activates native registrations on the UI thread after lightweight settings are assembled.</summary>
    public void Start()
    {
        if (_disposed || _started) return;
        _started = true;
        foreach (var binding in _bindings.Values.ToArray()) _bindings[binding.Action] = Register(binding);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ShortcutEditResult ChangeBinding(ReadingAction action, ShortcutGesture? gesture)
    {
        if (_disposed || IsRecording) return new(ShortcutBindingError.Unavailable);
        if (gesture is { } candidate && (!ShortcutGesture.TryParse(candidate.ToString(), out var parsed) || parsed != candidate))
            return new(ShortcutBindingError.Invalid);
        var conflict = _bindings.Values.FirstOrDefault(binding => binding.Action != action && gesture is not null && binding.Gesture == gesture);
        if (conflict is not null) return new(ShortcutBindingError.Duplicate, conflict.Action);
        var previous = _bindings[action];
        if (previous.Error == ShortcutBindingError.None && previous.Gesture == gesture
            && (gesture is null || previous.RegistrationId is not null)) return new();
        var replacement = Register(new(action, gesture));
        if (replacement.Error != ShortcutBindingError.None) return new(replacement.Error, NativeError: replacement.NativeError);
        try { _settings.SetValue(SettingsKey(action), gesture?.ToString() ?? ""); }
        catch (Exception exception)
        {
            if (replacement.RegistrationId is { } trial) _registrar.Unregister(trial);
            _logger?.LogError(exception, "Failed to save keyboard binding for {0}.", action);
            return new(ShortcutBindingError.SaveFailed);
        }
        _bindings[action] = replacement;
        if (previous.RegistrationId is { } old) _registrar.Unregister(old);
        Changed?.Invoke(this, EventArgs.Empty);
        return new();
    }

    public bool BeginRecording(ReadingAction action)
    {
        if (_disposed || IsRecording) return false;
        RecordingAction = action;
        foreach (var binding in _bindings.Values.ToArray())
        {
            if (binding.RegistrationId is { } id) _registrar.Unregister(id);
            _bindings[binding.Action] = binding with { RegistrationId = null };
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void EndRecording()
    {
        if (_disposed || !IsRecording) return;
        RecordingAction = null;
        foreach (var binding in _bindings.Values.ToArray())
            _bindings[binding.Action] = Register(binding with
            {
                Error = binding.Error == ShortcutBindingError.Unavailable ? ShortcutBindingError.None : binding.Error,
            });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ShortcutBinding Register(ShortcutBinding binding)
    {
        if (!_started || binding.Gesture is not { } gesture || binding.Error != ShortcutBindingError.None) return binding;
        do { _nextId = _nextId % 0xBFFF + 1; }
        while (_bindings.Values.Any(value => value.RegistrationId == _nextId));
        var id = _nextId;
        return _registrar.TryRegister(id, gesture, out var error)
            ? binding with { RegistrationId = id, NativeError = 0 }
            : binding with { Error = ShortcutBindingError.Unavailable, NativeError = error };
    }

    private void OnTriggered(object? sender, int id)
    {
        if (_disposed || IsRecording || _actions is null) return;
        var binding = _bindings.Values.FirstOrDefault(value => value.RegistrationId == id);
        if (binding is not null && _actions.CanExecute(binding.Action)) _ = DispatchAsync(_actions, binding.Action);
    }

    private async Task DispatchAsync(IReadingActions actions, ReadingAction action)
    {
        try { await actions.ExecuteAsync(action); }
        catch (Exception exception) { _logger?.LogError(exception, "Keyboard action {0} failed.", action); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _actions = null;
        _registrar.Triggered -= OnTriggered;
        foreach (var binding in _bindings.Values)
            if (binding.RegistrationId is { } id) _registrar.Unregister(id);
    }
}

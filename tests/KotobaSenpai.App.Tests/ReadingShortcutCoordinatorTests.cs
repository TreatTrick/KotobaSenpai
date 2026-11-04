using KotobaSenpai.App.Reading;
using KotobaSenpai.App.Shortcuts;
using KotobaSenpai.Core.Shortcuts;
using KotobaSenpai.App.ViewModels;
using System.Globalization;

namespace KotobaSenpai.App.Tests;

public sealed class ReadingShortcutCoordinatorTests
{
    [Fact]
    public void Invalid_saved_bindings_can_be_cleared_and_do_not_return_after_restart()
    {
        var settings = new Settings();
        settings.SetValue(ReadingShortcutCoordinator.SettingsKey(ReadingAction.Recognize), "F12");
        using (var shortcuts = Create(settings, new Registrar()))
        {
            Assert.Equal(ShortcutBindingError.Invalid, shortcuts.GetBinding(ReadingAction.Recognize).Error);
            Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, null).Succeeded);
            Assert.Equal(ShortcutBindingError.None, shortcuts.GetBinding(ReadingAction.Recognize).Error);
        }
        using var restarted = Create(settings, new Registrar());
        Assert.Null(restarted.GetBinding(ReadingAction.Recognize).Gesture);
        Assert.Equal(ShortcutBindingError.None, restarted.GetBinding(ReadingAction.Recognize).Error);
    }

    [Fact]
    public void A_successful_edit_does_not_mask_a_later_native_registration_failure()
    {
        var registrar = new Registrar();
        using var shortcuts = Create(new Settings(), registrar);
        using var model = new ShortcutSettingsViewModel(shortcuts, LocalizerFactory.Create(CultureInfo.GetCultureInfo("en")));
        model.Recognize.ClearCommand.Execute(null);
        model.Recognize.ResetCommand.Execute(null);
        registrar.RejectedKey = "F1";
        shortcuts.BeginRecording(ReadingAction.SelectRegion);
        shortcuts.EndRecording();
        Assert.Contains("1409", model.Recognize.Status);
        Assert.Contains("Inactive", model.Recognize.Hint);
    }

    [Fact]
    public void Visible_hints_follow_the_committed_binding_and_language_changes()
    {
        using var shortcuts = Create(new Settings(), new Registrar());
        var localizer = LocalizerFactory.Create(CultureInfo.GetCultureInfo("en"));
        using var model = new ShortcutSettingsViewModel(shortcuts, localizer);
        Assert.Equal("F1", model.Recognize.Hint);
        Assert.True(ShortcutGesture.TryParse("Ctrl+H", out var gesture));
        Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, gesture).Succeeded);
        Assert.Equal("Ctrl+H", model.Recognize.Hint);
        var previousName = model.Recognize.Name;
        localizer.ApplyCulture(CultureInfo.GetCultureInfo("zh-CN"));
        Assert.NotEqual(previousName, model.Recognize.Name);
        Assert.Equal("Ctrl+H", model.Recognize.Hint);
        model.Recognize.ClearCommand.Execute(null);
        Assert.Empty(model.Recognize.Hint);
        model.Recognize.ResetCommand.Execute(null);
        Assert.Equal("F1", model.Recognize.Hint);
    }

    [Fact]
    public void Configured_but_unavailable_bindings_are_explicitly_marked_inactive()
    {
        using var shortcuts = Create(new Settings(), new Registrar { RejectedKey = "F3" });
        using var model = new ShortcutSettingsViewModel(shortcuts, LocalizerFactory.Create(CultureInfo.GetCultureInfo("en")));
        Assert.Contains("F3", model.Stop.Hint);
        Assert.Contains("Inactive", model.Stop.Hint);
        Assert.Contains("1409", model.Stop.Status);
    }

    [Fact]
    public void Defaults_dispatch_and_recording_temporarily_releases_existing_keys()
    {
        var registrar = new Registrar();
        using var shortcuts = Create(new Settings(), registrar);
        var actions = new Actions();
        shortcuts.Attach(actions);
        var binding = shortcuts.GetBinding(ReadingAction.ToggleVisibility);
        Assert.Equal("F4", binding.Gesture?.ToString());
        registrar.Press(binding.RegistrationId!.Value);
        Assert.Equal([ReadingAction.ToggleVisibility], actions.Executed);

        shortcuts.BeginRecording(ReadingAction.Recognize);
        Assert.Empty(registrar.Registered);
        registrar.Press(binding.RegistrationId.Value);
        Assert.Single(actions.Executed);
        shortcuts.EndRecording();
        Assert.Equal(5, registrar.Registered.Count);
    }

    [Fact]
    public void Failed_registration_or_persistence_preserves_the_previous_binding()
    {
        var settings = new Settings();
        var registrar = new Registrar();
        using var shortcuts = Create(settings, registrar);
        var previous = shortcuts.GetBinding(ReadingAction.Recognize);
        registrar.Reject = true;
        Assert.False(shortcuts.ChangeBinding(ReadingAction.Recognize, new("F6")).Succeeded);
        Assert.Equal(previous, shortcuts.GetBinding(ReadingAction.Recognize));
        registrar.Reject = false;
        settings.Reject = true;
        Assert.False(shortcuts.ChangeBinding(ReadingAction.Recognize, new("F6")).Succeeded);
        Assert.Equal(previous, shortcuts.GetBinding(ReadingAction.Recognize));
        Assert.Equal(5, registrar.Registered.Count);
    }

    [Fact]
    public void Duplicate_keys_are_rejected_and_cleared_bindings_survive_restart()
    {
        var settings = new Settings();
        using (var shortcuts = Create(settings, new Registrar()))
        {
            Assert.False(shortcuts.ChangeBinding(ReadingAction.Recognize, new("F2")).Succeeded);
            Assert.True(shortcuts.ChangeBinding(ReadingAction.Recognize, null).Succeeded);
        }
        using var restarted = Create(settings, new Registrar());
        Assert.Null(restarted.GetBinding(ReadingAction.Recognize).Gesture);
        Assert.Equal("F2", restarted.GetBinding(ReadingAction.ToggleAutomatic).Gesture?.ToString());
    }

    [Fact]
    public void Startup_conflict_affects_one_binding_and_disposal_releases_all_keys()
    {
        var registrar = new Registrar { RejectedKey = "F3" };
        var shortcuts = Create(new Settings(), registrar);
        Assert.Null(shortcuts.GetBinding(ReadingAction.Stop).RegistrationId);
        Assert.Equal(4, registrar.Registered.Count);
        shortcuts.Dispose();
        Assert.Empty(registrar.Registered);
    }

    private static ReadingShortcutCoordinator Create(Settings settings, Registrar registrar)
    {
        var coordinator = new ReadingShortcutCoordinator(settings, registrar);
        coordinator.Start();
        return coordinator;
    }

    private sealed class Registrar : IGlobalShortcutRegistrar
    {
        public readonly Dictionary<int, ShortcutGesture> Registered = [];
        public bool Reject { get; set; }
        public string? RejectedKey { get; set; }
        public event EventHandler<int>? Triggered;
        public void Press(int id) => Triggered?.Invoke(this, id);
        public bool TryRegister(int id, ShortcutGesture gesture, out int error)
        {
            error = Reject || gesture.Key == RejectedKey ? 1409 : 0;
            if (error != 0) return false;
            Registered.Add(id, gesture);
            return true;
        }
        public void Unregister(int id) => Registered.Remove(id);
        public void Dispose() => Registered.Clear();
    }

    private sealed class Settings : KotobaSenpai.TestSupport.SettingsStub
    {
        private readonly Dictionary<string, string?> _values = [];
        public bool Reject { get; set; }
        public override string? GetValue(string key) => _values.GetValueOrDefault(key);
        public override void SetValue(string key, string? value)
        {
            if (Reject) throw new System.IO.IOException("Settings write failed.");
            _values[key] = value;
        }
    }

    private sealed class Actions : IReadingActions
    {
        public readonly List<ReadingAction> Executed = [];
        public bool CanExecute(ReadingAction action) => true;
        public Task ExecuteAsync(ReadingAction action) { Executed.Add(action); return Task.CompletedTask; }
    }
}

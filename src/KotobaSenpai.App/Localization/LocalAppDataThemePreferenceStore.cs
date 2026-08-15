using KotobaSenpai.App.Themes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// Implementation of <see cref="IThemePreferenceStore"/>: reads/writes the <c>Theme</c> field of settings.json through <see cref="ISettingsService"/>.
/// File I/O and unknown-field preservation are handled uniformly by the settings service; this class only handles the theme mode's enum parsing and fallback.
/// A missing or corrupt file is treated by the settings service as no preference (returns null), so it does not crash at startup.
/// </summary>
public sealed class LocalAppDataThemePreferenceStore : IThemePreferenceStore
{
    private const string Key = "Theme";
    private readonly ISettingsService _settings;

    public LocalAppDataThemePreferenceStore(ISettingsService settings) => _settings = settings;

    /// <inheritdoc />
    public AppThemeMode? Load()
    {
        var value = _settings.GetValue(Key);
        if (value is null)
            return null;

        return Enum.TryParse<AppThemeMode>(value, ignoreCase: true, out var mode) ? mode : null;
    }

    /// <inheritdoc />
    public void Save(AppThemeMode mode) => _settings.SetValue(Key, mode.ToString());
}

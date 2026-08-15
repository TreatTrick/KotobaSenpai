using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// Implementation of <see cref="ILanguagePreferenceStore"/>: reads/writes the <c>Language</c> field of settings.json through <see cref="ISettingsService"/>.
/// File I/O and unknown-field preservation are handled uniformly by the settings service; this class only handles the culture name's storage and whitespace validation.
/// A missing or corrupt file is treated by the settings service as no preference (returns null), so it does not crash at startup.
/// </summary>
public sealed class LocalAppDataLanguagePreferenceStore : ILanguagePreferenceStore
{
    private const string Key = "Language";
    private readonly ISettingsService _settings;

    public LocalAppDataLanguagePreferenceStore(ISettingsService settings) => _settings = settings;

    /// <inheritdoc />
    public string? Load()
    {
        var language = _settings.GetValue(Key);
        return string.IsNullOrWhiteSpace(language) ? null : language;
    }

    /// <inheritdoc />
    public void Save(string cultureName) => _settings.SetValue(Key, cultureName);
}

using KotobaSenpai.App.Themes;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// Theme-preference persistence port: saves/restores the user's chosen theme mode (Auto/Light/Dark) across restarts.
/// Symmetric to <see cref="ILanguagePreferenceStore"/>; minimal JSON persistence, to migrate once the settings module lands.
/// Abstracted as a port so tests can validate the restore logic with an in-memory fake without touching disk.
/// </summary>
public interface IThemePreferenceStore
{
    /// <summary>Reads the persisted theme mode; returns null when absent or corrupt (caller falls back to default Auto).</summary>
    AppThemeMode? Load();

    /// <summary>Persists the theme mode.</summary>
    void Save(AppThemeMode mode);
}

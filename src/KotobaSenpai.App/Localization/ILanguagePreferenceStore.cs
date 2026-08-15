namespace KotobaSenpai.App.Localization;

/// <summary>
/// Language-preference persistence port: saves/restores the user's chosen language across restarts. Minimal JSON persistence, to migrate once the settings module lands.
/// Abstracted as a port so tests can validate the restore logic with an in-memory fake without touching disk.
/// </summary>
public interface ILanguagePreferenceStore
{
    /// <summary>Reads the persisted language preference (culture name); returns null when absent or corrupt.</summary>
    string? Load();

    /// <summary>Persists the language preference (culture name).</summary>
    void Save(string cultureName);
}

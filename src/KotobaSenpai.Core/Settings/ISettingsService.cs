namespace KotobaSenpai.Core.Settings;

/// <summary>
/// Port for reading/writing user settings: the sole owner of
/// <c>%LocalAppData%/KotobaSenpai/settings.json</c>, storing raw string values keyed by string. A cross-cutting
/// port (same style as <see cref="KotobaSenpai.Core.Logging.ILogger"/> and
/// <see cref="KotobaSenpai.Core.Localization.IStringLocalizer"/>), agnostic to domain types — typed parsing
/// (theme enums, log levels, culture names) is handled by feature facades (preference store, <c>LogConfiguration</c>).
/// The port does not reference <see cref="System.IO"/> / <see cref="System.Text.Json"/> or any feature type.
/// Abstracting it as a port lets tests verify restore and parsing logic with an in-memory fake without touching disk.
/// </summary>
public interface ISettingsService
{
    /// <summary>Reads the raw string value for a key; returns null when the key doesn't exist or its value is null (the caller falls back to a default).</summary>
    string? GetValue(string key);

    /// <summary>Writes a key/value (implementations write through to disk and preserve other unknown fields).</summary>
    void SetValue(string key, string? value);
}

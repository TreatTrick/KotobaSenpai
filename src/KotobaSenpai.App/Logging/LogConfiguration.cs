using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// Reads the minimum log level: through <see cref="ISettingsService"/> it takes the <c>MinimumLogLevel</c> field (e.g.
/// "Error"/"Warning"/"Information") and parses it into a <see cref="LogLevel"/>; when the field is missing or parsing fails it falls back to
/// <see cref="LogLevel.Error"/>. File I/O and fault tolerance are handled uniformly by the settings service; this class only parses strings to levels.
/// </summary>
public static class LogConfiguration
{
    /// <summary>Reads the minimum log level; <paramref name="settings"/> is injected to make it testable.</summary>
    public static LogLevel LoadMinimumLevel(ISettingsService settings)
    {
        var raw = settings.GetValue("MinimumLogLevel");
        if (raw is not null && Enum.TryParse(raw, ignoreCase: true, out LogLevel level))
            return level;

        return LogLevel.Error;
    }
}

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Settings;

/// <summary>
/// File implementation of <see cref="ISettingsService"/>: the single owner of <c>%LocalAppData%/KotobaSenpai/settings.json</c>.
/// Singleton, loaded into an in-memory <see cref="JsonObject"/> during construction with write-through persistence; unknown fields are preserved
/// (<c>Language</c>/<c>Theme</c>/<c>MinimumLogLevel</c> coexist without overwriting each other); a missing or corrupt file is treated as an empty object, never throwing.
/// All reads and writes are serialized through an internal <see cref="lock"/>. Replaces the per-file read logic previously scattered across
/// <c>LocalAppDataSettingsFile</c> and <c>LogConfiguration</c>.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly JsonObject _settings;

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KotobaSenpai",
        "settings.json");

    /// <summary>Uses the real <see cref="DefaultFilePath"/>.</summary>
    public SettingsService() : this(DefaultFilePath) { }

    /// <summary><paramref name="filePath"/> is injected so tests can point at a temporary file.</summary>
    public SettingsService(string filePath)
    {
        _filePath = filePath;
        _settings = LoadOrEmpty();
    }

    /// <inheritdoc />
    public string? GetValue(string key)
    {
        lock (_gate)
        {
            if (!_settings.TryGetPropertyValue(key, out JsonNode? node) || node is null)
                return null;

            // When the node is not a string (e.g. a number or object), fall back to null, consistent with the existing preference store's fault-tolerance semantics.
            try
            {
                return node.GetValue<string>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public void SetValue(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null)
                _settings.Remove(key);
            else
                _settings[key] = value;
            Save();
        }
    }

    private JsonObject LoadOrEmpty()
    {
        if (!File.Exists(_filePath))
            return new JsonObject();

        try
        {
            if (JsonNode.Parse(File.ReadAllText(_filePath)) is JsonObject obj)
                return obj;
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return new JsonObject();
    }

    /// <summary>Write-through: serializes the entire in-memory object (preserving unknown fields), including automatic directory creation.</summary>
    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, _settings.ToJsonString(SerializerOptions));
    }
}

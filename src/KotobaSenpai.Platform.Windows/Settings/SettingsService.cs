using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.Platform.Windows.Settings;

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
    private JsonObject _settings;
    private readonly bool _readOnly;

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KotobaSenpai",
        "settings.json");

    /// <summary>Uses the real <see cref="DefaultFilePath"/>.</summary>
    public SettingsService() : this(DefaultFilePath) { }

    /// <summary><paramref name="filePath"/> is injected so tests can point at a temporary file.</summary>
    public SettingsService(string filePath, bool readOnly = false)
    {
        _filePath = filePath;
        _readOnly = readOnly;
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
        => TryUpdate(new Dictionary<string, string?>(), new Dictionary<string, string?> { [key] = value });

    public IReadOnlyDictionary<string, string?> GetSnapshot(IEnumerable<string> keys)
    {
        lock (_gate)
            return keys.ToDictionary(key => key, GetValue, StringComparer.Ordinal);
    }

    public bool TryUpdate(IReadOnlyDictionary<string, string?> expected, IReadOnlyDictionary<string, string?> changes)
    {
        if (_readOnly)
            throw new InvalidOperationException("This settings instance is read-only.");
        lock (_gate)
        {
            if (expected.Any(entry => GetValue(entry.Key) != entry.Value))
                return false;
            var next = (JsonObject)_settings.DeepClone();
            foreach (var (key, value) in changes)
                if (value is null)
                    next.Remove(key);
                else
                    next[key] = value;
            Save(next);
            _settings = next;
            return true;
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
    private void Save(JsonObject next)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, next.ToJsonString(SerializerOptions));
            if (File.Exists(_filePath))
                File.Replace(temporary, _filePath, null);
            else
                File.Move(temporary, _filePath);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

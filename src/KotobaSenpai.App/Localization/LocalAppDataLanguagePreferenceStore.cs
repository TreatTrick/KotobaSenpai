using System.IO;
using System.Text.Json;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="ILanguagePreferenceStore"/> 实现：读写 <c>%LocalAppData%/KotobaSenpai/settings.json</c>。
/// 文件不存在或损坏时视为无偏好（返回 null），不在启动时崩溃。
/// </summary>
public sealed class LocalAppDataLanguagePreferenceStore : ILanguagePreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public LocalAppDataLanguagePreferenceStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KotobaSenpai");
        _filePath = Path.Combine(directory, "settings.json");
    }

    /// <inheritdoc />
    public string? Load()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var settings = JsonSerializer.Deserialize<LanguageSettings>(File.ReadAllText(_filePath));
            return string.IsNullOrWhiteSpace(settings?.Language) ? null : settings.Language;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(string cultureName)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath,
            JsonSerializer.Serialize(new LanguageSettings { Language = cultureName }, SerializerOptions));
    }

    private sealed class LanguageSettings
    {
        public string? Language { get; set; }
    }
}

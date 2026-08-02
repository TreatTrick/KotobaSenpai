using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// 读写 <c>%LocalAppData%/KotobaSenpai/settings.json</c> 的最小共享助手。
/// 以可变 <see cref="JsonObject"/> 读-改-写，保留未知字段（<c>Language</c> / <c>Theme</c> / <c>MinimumLogLevel</c> 共存），
/// 使语言与主题偏好存储互不覆盖。文件缺失或损坏时视为空对象，不抛异常。
/// </summary>
internal static class LocalAppDataSettingsFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KotobaSenpai",
        "settings.json");

    /// <summary>读取 settings.json 为可变 <see cref="JsonObject"/>；文件缺失或损坏时返回空对象。</summary>
    public static JsonObject LoadOrEmpty()
    {
        if (!File.Exists(FilePath))
            return new JsonObject();

        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject obj)
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

    /// <summary>写回 <see cref="JsonObject"/>（含目录自动创建）。</summary>
    public static void Save(JsonObject obj)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        File.WriteAllText(FilePath, obj.ToJsonString(SerializerOptions));
    }
}

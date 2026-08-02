using System.IO;
using System.Text.Json;
using KotobaSenpai.Core.Logging;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// 读取最小日志级别：从 <c>%LocalAppData%/KotobaSenpai/settings.json</c> 的可选字段
/// <c>MinimumLogLevel</c>（如 "Error"/"Warning"/"Information"）解析为 <see cref="LogLevel"/>；
/// 文件不存在、字段缺失或解析失败时回退 <see cref="LogLevel.Error"/>。复用 add-i18n 的
/// settings.json 缝隙（BCL <see cref="JsonDocument"/>，对未知字段容忍），无新 NuGet。
/// </summary>
public static class LogConfiguration
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KotobaSenpai", "settings.json");

    /// <summary>读取最小日志级别；<paramref name="filePath"/> 注入便于测试。</summary>
    public static LogLevel LoadMinimumLevel(string? filePath = null)
    {
        var path = filePath ?? DefaultPath;
        try
        {
            if (!File.Exists(path))
                return LogLevel.Error;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("MinimumLogLevel", out var prop)
                && prop.ValueKind == JsonValueKind.String
                && Enum.TryParse(prop.GetString(), ignoreCase: true, out LogLevel level))
                return level;
        }
        catch (IOException) { }
        catch (JsonException) { }

        return LogLevel.Error;
    }
}

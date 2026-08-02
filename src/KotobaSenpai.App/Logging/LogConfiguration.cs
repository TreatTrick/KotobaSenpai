using KotobaSenpai.Core.Logging;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Logging;

/// <summary>
/// 读取最小日志级别：经 <see cref="ISettingsService"/> 取 <c>MinimumLogLevel</c> 字段（如
/// "Error"/"Warning"/"Information"）解析为 <see cref="LogLevel"/>；字段缺失或解析失败时回退
/// <see cref="LogLevel.Error"/>。文件 I/O 与容错由设置服务统一承担，本类只负责字符串到级别的解析。
/// </summary>
public static class LogConfiguration
{
    /// <summary>读取最小日志级别；<paramref name="settings"/> 注入便于测试。</summary>
    public static LogLevel LoadMinimumLevel(ISettingsService settings)
    {
        var raw = settings.GetValue("MinimumLogLevel");
        if (raw is not null && Enum.TryParse(raw, ignoreCase: true, out LogLevel level))
            return level;

        return LogLevel.Error;
    }
}

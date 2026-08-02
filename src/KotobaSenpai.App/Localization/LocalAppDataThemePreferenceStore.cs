using System;
using System.Text.Json.Nodes;
using KotobaSenpai.App.Themes;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="IThemePreferenceStore"/> 实现：读写 <c>%LocalAppData%/KotobaSenpai/settings.json</c> 的 <c>Theme</c> 字段。
/// 经共享助手以可变 <see cref="JsonObject"/> 读-改-写，保留 <c>Language</c>/<c>MinimumLogLevel</c> 等其他字段。
/// 文件不存在或损坏时视为无偏好（返回 null），不在启动时崩溃。
/// </summary>
public sealed class LocalAppDataThemePreferenceStore : IThemePreferenceStore
{
    /// <inheritdoc />
    public AppThemeMode? Load()
    {
        JsonObject settings = LocalAppDataSettingsFile.LoadOrEmpty();
        if (!settings.TryGetPropertyValue("Theme", out JsonNode? node) || node is null)
            return null;

        try
        {
            return Enum.TryParse<AppThemeMode>(node.GetValue<string>(), ignoreCase: true, out var mode)
                ? mode
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(AppThemeMode mode)
    {
        JsonObject settings = LocalAppDataSettingsFile.LoadOrEmpty();
        settings["Theme"] = mode.ToString();
        LocalAppDataSettingsFile.Save(settings);
    }
}

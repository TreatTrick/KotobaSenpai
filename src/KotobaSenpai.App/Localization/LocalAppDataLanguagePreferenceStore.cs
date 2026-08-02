using System;
using System.Text.Json.Nodes;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="ILanguagePreferenceStore"/> 实现：读写 <c>%LocalAppData%/KotobaSenpai/settings.json</c> 的 <c>Language</c> 字段。
/// 经共享助手以可变 <see cref="JsonObject"/> 读-改-写，保留 <c>Theme</c>/<c>MinimumLogLevel</c> 等其他字段，
/// 使切换语言不会丢失主题偏好。文件不存在或损坏时视为无偏好（返回 null），不在启动时崩溃。
/// </summary>
public sealed class LocalAppDataLanguagePreferenceStore : ILanguagePreferenceStore
{
    /// <inheritdoc />
    public string? Load()
    {
        JsonObject settings = LocalAppDataSettingsFile.LoadOrEmpty();
        if (!settings.TryGetPropertyValue("Language", out JsonNode? node) || node is null)
            return null;

        try
        {
            string language = node.GetValue<string>();
            return string.IsNullOrWhiteSpace(language) ? null : language;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(string cultureName)
    {
        JsonObject settings = LocalAppDataSettingsFile.LoadOrEmpty();
        settings["Language"] = cultureName;
        LocalAppDataSettingsFile.Save(settings);
    }
}

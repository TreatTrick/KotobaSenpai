using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="ILanguagePreferenceStore"/> 实现：经 <see cref="ISettingsService"/> 读写 settings.json 的
/// <c>Language</c> 字段。文件 I/O 与未知字段保留由设置服务统一承担；本类只负责 culture 名的存取与空白校验。
/// 文件不存在或损坏时经设置服务视为无偏好（返回 null），不在启动时崩溃。
/// </summary>
public sealed class LocalAppDataLanguagePreferenceStore : ILanguagePreferenceStore
{
    private const string Key = "Language";
    private readonly ISettingsService _settings;

    public LocalAppDataLanguagePreferenceStore(ISettingsService settings) => _settings = settings;

    /// <inheritdoc />
    public string? Load()
    {
        var language = _settings.GetValue(Key);
        return string.IsNullOrWhiteSpace(language) ? null : language;
    }

    /// <inheritdoc />
    public void Save(string cultureName) => _settings.SetValue(Key, cultureName);
}

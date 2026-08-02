using KotobaSenpai.App.Themes;
using KotobaSenpai.Core.Settings;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// <see cref="IThemePreferenceStore"/> 实现：经 <see cref="ISettingsService"/> 读写 settings.json 的
/// <c>Theme</c> 字段。文件 I/O 与未知字段保留由设置服务统一承担；本类只负责主题模式的枚举解析与回退。
/// 文件不存在或损坏时经设置服务视为无偏好（返回 null），不在启动时崩溃。
/// </summary>
public sealed class LocalAppDataThemePreferenceStore : IThemePreferenceStore
{
    private const string Key = "Theme";
    private readonly ISettingsService _settings;

    public LocalAppDataThemePreferenceStore(ISettingsService settings) => _settings = settings;

    /// <inheritdoc />
    public AppThemeMode? Load()
    {
        var value = _settings.GetValue(Key);
        if (value is null)
            return null;

        return Enum.TryParse<AppThemeMode>(value, ignoreCase: true, out var mode) ? mode : null;
    }

    /// <inheritdoc />
    public void Save(AppThemeMode mode) => _settings.SetValue(Key, mode.ToString());
}

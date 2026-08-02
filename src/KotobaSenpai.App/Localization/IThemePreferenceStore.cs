using KotobaSenpai.App.Themes;

namespace KotobaSenpai.App.Localization;

/// <summary>
/// 主题偏好持久化端口：跨重启保存/恢复用户选择的主题模式（Auto/Light/Dark）。
/// 与 <see cref="ILanguagePreferenceStore"/> 对称，最小 JSON 持久化，待设置模块落地后迁移。
/// 抽象为端口便于在测试中以 in-memory fake 验证恢复逻辑，无需触碰磁盘。
/// </summary>
public interface IThemePreferenceStore
{
    /// <summary>读取已持久化的主题模式；不存在或损坏时返回 null（调用方回退默认 Auto）。</summary>
    AppThemeMode? Load();

    /// <summary>持久化主题模式。</summary>
    void Save(AppThemeMode mode);
}

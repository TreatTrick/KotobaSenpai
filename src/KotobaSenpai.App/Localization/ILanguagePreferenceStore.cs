namespace KotobaSenpai.App.Localization;

/// <summary>
/// 语言偏好持久化端口：跨重启保存/恢复用户选择的语言。最小 JSON 持久化，待设置模块落地后迁移。
/// 抽象为端口便于在测试中以 in-memory fake 验证恢复逻辑，无需触碰磁盘。
/// </summary>
public interface ILanguagePreferenceStore
{
    /// <summary>读取已持久化的语言偏好（culture 名）；不存在或损坏时返回 null。</summary>
    string? Load();

    /// <summary>持久化语言偏好（culture 名）。</summary>
    void Save(string cultureName);
}

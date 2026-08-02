namespace KotobaSenpai.Core.Settings;

/// <summary>
/// 用户设置读写端口：作为 <c>%LocalAppData%/KotobaSenpai/settings.json</c> 的唯一归属，
/// 按字符串键存取原始字符串值。跨切面端口（与 <see cref="KotobaSenpai.Core.Logging.ILogger"/>、
/// <see cref="KotobaSenpai.Core.Localization.IStringLocalizer"/> 同风格），领域类型无关--
/// 类型化解析（主题枚举、日志级别、culture 名）由各特性门面（偏好存储、<c>LogConfiguration</c>）承担，
/// 端口不引用 <see cref="System.IO"/> / <see cref="System.Text.Json"/> 或任何特性类型。
/// 抽象为端口便于在测试中以 in-memory fake 验证恢复与解析逻辑，无需触碰磁盘。
/// </summary>
public interface ISettingsService
{
    /// <summary>读取键对应的原始字符串值；键不存在或值为 null 时返回 null（调用方回退默认）。</summary>
    string? GetValue(string key);

    /// <summary>写入键值（实现写穿到磁盘并保留其他未知字段）。</summary>
    void SetValue(string key, string? value);
}

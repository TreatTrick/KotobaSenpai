namespace KotobaSenpai.Core.Localization;

/// <summary>
/// 本地化端口：按资源键（及可选格式参数）解析为当前文化的本地化字符串，
/// 并在运行时文化切换时通过 <see cref="CultureChanged"/> 通知订阅者即时刷新。
/// <para>
/// 端口位于 Core（零外部依赖，BCL-only）；具体实现位于 App，ViewModel 仅依赖本接口，
/// 故可在无桌面的测试中以 fake 验证。
/// </para>
/// </summary>
public interface IStringLocalizer
{
    /// <summary>按键解析本地化字符串；资源值中的 {0} 占位符由 <paramref name="args"/> 替换。</summary>
    string Get(string key, params object[] args);

    /// <summary>当前 UI 文化在运行时切换后触发，订阅者应据此重算已显示的本地化属性。</summary>
    event EventHandler? CultureChanged;
}

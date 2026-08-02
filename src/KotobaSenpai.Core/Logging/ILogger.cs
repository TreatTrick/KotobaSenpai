namespace KotobaSenpai.Core.Logging;

/// <summary>
/// 跨切面日志端口。位于 Core（零外部依赖），文件实现在 App；ViewModel 仅依赖本端口，
/// 与 <c>IStringLocalizer</c> 端口-适配器风格一致。端口不引用 <c>Core.Localization</c>：
/// 用户可见异常的 <c>ErrorCode</c> 提取由 App 实现完成，保持日志与本地化两个跨切面端口解耦。
/// </summary>
public interface ILogger
{
    /// <summary>写一条日志；<paramref name="exception"/> 非 null 时附带类型/消息/堆栈。</summary>
    void Log(LogLevel level, Exception? exception, string message, params object[] args);

    /// <summary>Error 级便捷重载（记异常）。</summary>
    void LogError(Exception exception, string message, params object[] args)
        => Log(LogLevel.Error, exception, message, args);

    /// <summary>Warning 级便捷重载。</summary>
    void LogWarning(string message, params object[] args)
        => Log(LogLevel.Warning, null, message, args);

    /// <summary>Information 级便捷重载。</summary>
    void LogInformation(string message, params object[] args)
        => Log(LogLevel.Information, null, message, args);
}

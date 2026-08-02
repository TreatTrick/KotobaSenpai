using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.SeedWork;

/// <summary>领域不变量规则；<see cref="IsBroken"/> 返回真时表示当前状态违反规则。</summary>
public interface IBusinessRule
{
    bool IsBroken();

    /// <summary>开发者可读、locale 无关的规则描述（仅用于日志/调试，不直接展示给用户）。</summary>
    string Message { get; }

    /// <summary>稳定、locale 无关的错误码，由表现层翻译为本地化用户消息。</summary>
    string ErrorCode { get; }
}

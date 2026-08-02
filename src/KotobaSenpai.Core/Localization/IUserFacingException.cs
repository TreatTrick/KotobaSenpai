namespace KotobaSenpai.Core.Localization;

/// <summary>
/// 用户可见异常的标记接口。Core/Platform 中消息可能漏到 UI 的异常实现本接口并暴露稳定
/// <see cref="ErrorCode"/>；App 表现层的 <c>IUserMessageResolver</c> 据此把码翻译为本地化消息，
/// 而不把原始异常文本当作已翻译文本直接展示。
/// </summary>
public interface IUserFacingException
{
    /// <summary>与 <c>ErrorCodes</c> 键对应的稳定错误码，永不为 null。</summary>
    string ErrorCode { get; }
}

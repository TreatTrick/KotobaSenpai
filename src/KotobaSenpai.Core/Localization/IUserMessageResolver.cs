namespace KotobaSenpai.Core.Localization;

/// <summary>
/// 表现层端口：把异常映射为本地化用户消息。若异常为 <see cref="IUserFacingException"/>，
/// 按其 <c>ErrorCode</c> 翻译；否则使用调用方提供的回退错误码。原始异常文本绝不作为已翻译文本展示。
/// 端口位于 Core（仅依赖 BCL <see cref="Exception"/>）；实现位于 App。
/// </summary>
public interface IUserMessageResolver
{
    /// <summary>解析异常为本地化用户消息：<paramref name="exception"/> 为用户可见异常时按其错误码翻译，否则按 <paramref name="fallbackErrorCode"/> 翻译。</summary>
    string Resolve(Exception exception, string fallbackErrorCode);
}

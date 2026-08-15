using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.Models;

/// <summary>
/// 聚合不变量被打破时抛出的异常。携带稳定 <see cref="ErrorCode"/>（供表现层翻译），
/// 实现用户可见异常标记接口；<see cref="Message"/> 仅为开发者可读文本，不直接展示给用户。
/// </summary>
public sealed class BusinessRuleValidationException : Exception, IUserFacingException
{
    public string ErrorCode { get; }

    public string Details { get; }

    public BusinessRuleValidationException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
        Details = message;
    }

    public override string ToString() => $"{GetType().FullName}: {Message}";
}
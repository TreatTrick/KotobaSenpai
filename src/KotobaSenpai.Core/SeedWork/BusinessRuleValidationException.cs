using KotobaSenpai.Core.Localization;

namespace KotobaSenpai.Core.SeedWork;

/// <summary>
/// 聚合不变量被打破时抛出的异常。携带稳定 <see cref="ErrorCode"/>（供表现层翻译），
/// 实现用户可见异常标记接口；<see cref="Message"/> 仅为开发者可读文本，不直接展示给用户。
/// </summary>
public sealed class BusinessRuleValidationException : Exception, IUserFacingException
{
    public IBusinessRule BrokenRule { get; }

    public string Details { get; }

    string IUserFacingException.ErrorCode => BrokenRule.ErrorCode;

    public BusinessRuleValidationException(IBusinessRule brokenRule)
        : base(brokenRule.Message)
    {
        BrokenRule = brokenRule;
        Details = brokenRule.Message;
    }

    public override string ToString() => $"{BrokenRule.GetType().FullName}: {BrokenRule.Message}";
}

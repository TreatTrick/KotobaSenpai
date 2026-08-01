namespace KotobaSenpai.Core.SeedWork;

/// <summary>聚合不变量被打破时抛出的异常，携带可读的业务规则信息。</summary>
public sealed class BusinessRuleValidationException : Exception
{
    public IBusinessRule BrokenRule { get; }

    public string Details { get; }

    public BusinessRuleValidationException(IBusinessRule brokenRule)
        : base(brokenRule.Message)
    {
        BrokenRule = brokenRule;
        Details = brokenRule.Message;
    }

    public override string ToString() => $"{BrokenRule.GetType().FullName}: {BrokenRule.Message}";
}

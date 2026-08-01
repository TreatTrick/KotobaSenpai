namespace KotobaSenpai.Core.SeedWork;

/// <summary>
/// 实体基类。聚合根继承本类以获得领域事件收集与不变量校验能力。
/// 参见 modular-monolith-with-ddd 的 BuildingBlocks.Domain.Entity。
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>当前实体已产生但尚未分发的领域事件。</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>校验聚合不变量；规则被打破时抛出 <see cref="BusinessRuleValidationException"/>。</summary>
    protected static void CheckRule(IBusinessRule rule)
    {
        if (rule.IsBroken())
            throw new BusinessRuleValidationException(rule);
    }
}

namespace KotobaSenpai.Core.SeedWork;

/// <summary>由聚合根产生的领域事件标记接口。</summary>
public interface IDomainEvent
{
    Guid Id { get; }

    DateTime OccurredOn { get; }
}

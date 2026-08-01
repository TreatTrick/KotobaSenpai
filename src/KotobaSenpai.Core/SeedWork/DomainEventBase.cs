namespace KotobaSenpai.Core.SeedWork;

/// <summary>领域事件基类，提供唯一标识与发生时间。</summary>
public abstract class DomainEventBase : IDomainEvent
{
    protected DomainEventBase()
    {
        Id = Guid.NewGuid();
        OccurredOn = DateTime.UtcNow;
    }

    public Guid Id { get; }

    public DateTime OccurredOn { get; }
}

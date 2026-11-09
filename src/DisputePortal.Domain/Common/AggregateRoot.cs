namespace DisputePortal.Domain.Common;

/// <summary>Something that happened in the domain that other parts of the system may care about.</summary>
public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}

/// <summary>
/// Base for aggregate roots: the only objects repositories load and save, and the consistency boundary
/// for their children. Domain events are collected here and published after a successful save.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

using DisputePortal.Domain.Common;

namespace DisputePortal.Application.Abstractions;

/// <summary>A use case that changes state. One handler per command keeps each class to one reason to change.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>A use case that only reads.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reacts to a domain event after the change that raised it has been saved. Events are delivered from an
/// outbox at least once, so a handler may see the same event twice and must cope with that.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

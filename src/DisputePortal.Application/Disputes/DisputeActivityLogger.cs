using DisputePortal.Application.Abstractions;
using DisputePortal.Domain.Disputes;
using Microsoft.Extensions.Logging;

namespace DisputePortal.Application.Disputes;

/// <summary>
/// Writes a structured log line for every dispute lifecycle event. It's also the obvious place to hang
/// customer notifications later: add another <see cref="IDomainEventHandler{TEvent}"/>, change nothing else.
/// </summary>
internal sealed partial class DisputeActivityLogger(ILogger<DisputeActivityLogger> logger) :
    IDomainEventHandler<DisputeOpened>,
    IDomainEventHandler<DisputeReviewStarted>,
    IDomainEventHandler<DisputeResolved>,
    IDomainEventHandler<DisputeWithdrawn>
{
    public Task HandleAsync(DisputeOpened e, CancellationToken cancellationToken = default)
    {
        LogOpened(e.Reference, e.CustomerId, e.TransactionId, e.Reason);
        return Task.CompletedTask;
    }

    public Task HandleAsync(DisputeReviewStarted e, CancellationToken cancellationToken = default)
    {
        LogReviewStarted(e.Reference, e.AgentId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(DisputeResolved e, CancellationToken cancellationToken = default)
    {
        LogResolved(e.Reference, e.Outcome, e.AgentId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(DisputeWithdrawn e, CancellationToken cancellationToken = default)
    {
        LogWithdrawn(e.Reference, e.CustomerId);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispute {DisputeReference} opened by customer {CustomerId} for transaction {TransactionId} ({Reason})")]
    private partial void LogOpened(string disputeReference, string customerId, Guid transactionId, DisputeReason reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispute {DisputeReference} taken into review by agent {AgentId}")]
    private partial void LogReviewStarted(string disputeReference, string agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispute {DisputeReference} resolved as {Outcome} by agent {AgentId}")]
    private partial void LogResolved(string disputeReference, DisputeOutcome outcome, string agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispute {DisputeReference} withdrawn by customer {CustomerId}")]
    private partial void LogWithdrawn(string disputeReference, string customerId);
}

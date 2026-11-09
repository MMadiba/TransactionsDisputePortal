using DisputePortal.Domain.Common;

namespace DisputePortal.Domain.Disputes;

public sealed record DisputeOpened(
    Guid DisputeId, string Reference, Guid TransactionId, string CustomerId, DisputeReason Reason, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record DisputeReviewStarted(Guid DisputeId, string Reference, string AgentId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record DisputeResolved(
    Guid DisputeId, string Reference, string CustomerId, DisputeOutcome Outcome, string AgentId, DateTime OccurredAtUtc) : IDomainEvent;

public sealed record DisputeWithdrawn(Guid DisputeId, string Reference, string CustomerId, DateTime OccurredAtUtc) : IDomainEvent;

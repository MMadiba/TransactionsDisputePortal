using DisputePortal.Domain.Common;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Disputes;

/// <summary>
/// Aggregate root: a customer's dispute against one of their transactions.
/// Every state change goes through a method here, which enforces the lifecycle
/// (Submitted → UnderReview → Upheld/Declined, or Withdrawn while open), appends to the
/// <see cref="History"/> and raises a domain event, so status, audit trail and side effects can't drift apart.
/// </summary>
public sealed class Dispute : AggregateRoot
{
    private readonly List<DisputeHistoryEntry> _history = [];

    private Dispute() { } // EF Core

    public Guid Id { get; private set; }
    public string Reference { get; private set; } = default!;
    public Guid TransactionId { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public DisputeReason Reason { get; private set; }
    public string Details { get; private set; } = default!;
    public DisputeStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>Optimistic concurrency token, rotated on every change.</summary>
    public Guid Version { get; private set; }

    public IReadOnlyCollection<DisputeHistoryEntry> History => _history;

    public bool IsOpen => Status is DisputeStatus.Submitted or DisputeStatus.UnderReview;

    /// <summary>Whether this dispute stops another one being raised on the same transaction.</summary>
    public bool IsLive => Status != DisputeStatus.Withdrawn;

    /// <summary>
    /// Raises a dispute. The eligibility policy is applied here, inside the aggregate, so there is no way to
    /// create a dispute that breaks the rules.
    /// </summary>
    public static Dispute Open(
        Transaction transaction,
        Dispute? liveDispute,
        DisputeEligibilityPolicy policy,
        DisputeReason reason,
        string details,
        Actor customer,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(customer);

        if (customer.Role != ActorRole.Customer || !transaction.IsOwnedBy(customer.UserId))
        {
            throw new BusinessRuleViolationException("dispute.not_owner", "Only the account holder can dispute a transaction.");
        }

        var eligibility = policy.Evaluate(transaction, liveDispute, nowUtc);
        if (!eligibility.CanDispute)
        {
            throw eligibility.IsConflict
                ? new DomainConflictException(eligibility.Code!, eligibility.Message!)
                : new BusinessRuleViolationException(eligibility.Code!, eligibility.Message!);
        }

        details = details?.Trim() ?? string.Empty;
        if (details.Length is < DisputeLimits.DetailsMinLength or > DisputeLimits.DetailsMaxLength)
        {
            throw new BusinessRuleViolationException("dispute.details_length",
                $"Please describe the problem in {DisputeLimits.DetailsMinLength} to {DisputeLimits.DetailsMaxLength} characters.");
        }

        var dispute = new Dispute
        {
            Id = Guid.CreateVersion7(),
            Reference = DisputeReference.New(),
            TransactionId = transaction.Id,
            CustomerId = transaction.CustomerId,
            Reason = reason,
            Details = details,
            Status = DisputeStatus.Submitted,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Version = Guid.NewGuid()
        };

        dispute._history.Add(new DisputeHistoryEntry(DisputeStatus.Submitted, customer, null, nowUtc));
        dispute.Raise(new DisputeOpened(dispute.Id, dispute.Reference, dispute.TransactionId, dispute.CustomerId, reason, nowUtc));
        return dispute;
    }

    public void StartReview(Actor agent, DateTime nowUtc)
    {
        EnsureAgent(agent);
        if (Status != DisputeStatus.Submitted)
        {
            throw new DomainConflictException("dispute.invalid_state", $"You can't start a review on dispute {Reference} while it is {Status}.");
        }

        Transition(DisputeStatus.UnderReview, agent, null, nowUtc);
        Raise(new DisputeReviewStarted(Id, Reference, agent.UserId, nowUtc));
    }

    public void Resolve(DisputeOutcome outcome, string note, Actor agent, DateTime nowUtc)
    {
        EnsureAgent(agent);
        EnsureOpen("resolve");

        note = note?.Trim() ?? string.Empty;
        if (note.Length is < DisputeLimits.ResolutionNoteMinLength or > DisputeLimits.ResolutionNoteMaxLength)
        {
            throw new BusinessRuleViolationException("dispute.note_length",
                $"The note must be {DisputeLimits.ResolutionNoteMinLength} to {DisputeLimits.ResolutionNoteMaxLength} characters.");
        }

        ResolutionNote = note;
        ClosedAtUtc = nowUtc;
        Transition(outcome == DisputeOutcome.Upheld ? DisputeStatus.Upheld : DisputeStatus.Declined, agent, note, nowUtc);
        Raise(new DisputeResolved(Id, Reference, CustomerId, outcome, agent.UserId, nowUtc));
    }

    public void Withdraw(Actor customer, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (customer.Role != ActorRole.Customer || customer.UserId != CustomerId)
        {
            throw new BusinessRuleViolationException("dispute.not_owner", "Only the customer who raised a dispute can withdraw it.");
        }

        EnsureOpen("withdraw");
        ClosedAtUtc = nowUtc;
        Transition(DisputeStatus.Withdrawn, customer, null, nowUtc);
        Raise(new DisputeWithdrawn(Id, Reference, CustomerId, nowUtc));
    }

    private void Transition(DisputeStatus to, Actor actor, string? note, DateTime nowUtc)
    {
        Status = to;
        UpdatedAtUtc = nowUtc;
        Version = Guid.NewGuid();
        _history.Add(new DisputeHistoryEntry(to, actor, note, nowUtc));
    }

    private void EnsureOpen(string action)
    {
        if (!IsOpen)
        {
            throw new DomainConflictException("dispute.not_open",
                $"You can't {action} dispute {Reference} because it is already {Status.ToString().ToLowerInvariant()}.");
        }
    }

    private static void EnsureAgent(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Role != ActorRole.Agent)
        {
            throw new BusinessRuleViolationException("dispute.agent_only", "Only the disputes team can do that.");
        }
    }
}

/// <summary>Entity inside the <see cref="Dispute"/> aggregate: one entry in its audit trail.</summary>
public sealed class DisputeHistoryEntry
{
    private DisputeHistoryEntry() { } // EF Core

    internal DisputeHistoryEntry(DisputeStatus status, Actor actor, string? note, DateTime occurredAtUtc)
    {
        Status = status;
        ActorUserId = actor.UserId;
        ActorRole = actor.Role;
        ActorName = actor.Name;
        Note = note;
        OccurredAtUtc = occurredAtUtc;
    }

    // Left unset on purpose: EF Core generates the key when the entry is inserted, which is how it tells
    // entries appended to an already-loaded dispute (inserts) from existing ones.
    public Guid Id { get; private set; }
    public Guid DisputeId { get; private set; }
    public DisputeStatus Status { get; private set; }
    public string ActorUserId { get; private set; } = default!;
    public ActorRole ActorRole { get; private set; }
    public string ActorName { get; private set; } = default!;
    public string? Note { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

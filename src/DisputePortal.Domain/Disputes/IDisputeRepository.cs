namespace DisputePortal.Domain.Disputes;

public interface IDisputeRepository
{
    Task<Dispute?> GetByIdAsync(Guid disputeId, CancellationToken cancellationToken);

    /// <summary>The dispute, but only if it was raised by <paramref name="customerId"/>.</summary>
    Task<Dispute?> GetOwnedByAsync(Guid disputeId, string customerId, CancellationToken cancellationToken);

    /// <summary>The dispute stopping a new one on this transaction (see <see cref="Dispute.BlocksNewDisputes"/>), if any.</summary>
    Task<Dispute?> FindBlockingForTransactionAsync(Guid transactionId, CancellationToken cancellationToken);

    void Add(Dispute dispute);
}

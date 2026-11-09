namespace DisputePortal.Domain.Disputes;

public interface IDisputeRepository
{
    Task<Dispute?> GetByIdAsync(Guid disputeId, CancellationToken cancellationToken);

    /// <summary>The dispute, but only if it was raised by <paramref name="customerId"/>.</summary>
    Task<Dispute?> GetOwnedByAsync(Guid disputeId, string customerId, CancellationToken cancellationToken);

    /// <summary>The dispute currently blocking a new one on this transaction (anything not withdrawn), if any.</summary>
    Task<Dispute?> FindLiveForTransactionAsync(Guid transactionId, CancellationToken cancellationToken);

    void Add(Dispute dispute);
}

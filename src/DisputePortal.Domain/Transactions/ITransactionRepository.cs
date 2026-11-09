namespace DisputePortal.Domain.Transactions;

public interface ITransactionRepository
{
    /// <summary>The transaction, but only if it belongs to <paramref name="customerId"/>.</summary>
    Task<Transaction?> GetOwnedByAsync(Guid transactionId, string customerId, CancellationToken cancellationToken);
}

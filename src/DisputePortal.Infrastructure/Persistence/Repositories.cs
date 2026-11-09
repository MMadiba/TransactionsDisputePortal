using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Infrastructure.Persistence;

internal sealed class TransactionRepository(ApplicationDbContext db) : ITransactionRepository
{
    public Task<Transaction?> GetOwnedByAsync(Guid transactionId, string customerId, CancellationToken cancellationToken) =>
        db.Transactions.SingleOrDefaultAsync(t => t.Id == transactionId && t.CustomerId == customerId, cancellationToken);
}

internal sealed class DisputeRepository(ApplicationDbContext db) : IDisputeRepository
{
    // The whole aggregate (root + history) is always loaded together; it's the consistency boundary.
    private IQueryable<Dispute> Aggregates => db.Disputes.Include(d => d.History);

    public Task<Dispute?> GetByIdAsync(Guid disputeId, CancellationToken cancellationToken) =>
        Aggregates.SingleOrDefaultAsync(d => d.Id == disputeId, cancellationToken);

    public Task<Dispute?> GetOwnedByAsync(Guid disputeId, string customerId, CancellationToken cancellationToken) =>
        Aggregates.SingleOrDefaultAsync(d => d.Id == disputeId && d.CustomerId == customerId, cancellationToken);

    public Task<Dispute?> FindLiveForTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        Aggregates.FirstOrDefaultAsync(d => d.TransactionId == transactionId && d.Status != DisputeStatus.Withdrawn, cancellationToken);

    public void Add(Dispute dispute) => db.Disputes.Add(dispute);
}

/// <summary>
/// Saves everything tracked in this scope in one transaction, translates provider errors into
/// application exceptions, and only then publishes the domain events that were raised.
/// </summary>
internal sealed class UnitOfWork(ApplicationDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
        {
            SqliteErrorCode: SqliteConstraint,
            SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey
        })
        {
            throw new UniqueConstraintConflictException(ex);
        }

        aggregates.ForEach(a => a.ClearDomainEvents());
        await dispatcher.DispatchAsync(events, cancellationToken);
    }
}

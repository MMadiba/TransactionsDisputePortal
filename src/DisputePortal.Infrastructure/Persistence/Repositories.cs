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

    public Task<Dispute?> FindBlockingForTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        Aggregates.FirstOrDefaultAsync(d => d.TransactionId == transactionId && d.Status != DisputeStatus.Withdrawn, cancellationToken);

    public void Add(Dispute dispute) => db.Disputes.Add(dispute);
}

/// <summary>
/// Saves everything tracked in this scope in one transaction and translates provider errors into application
/// exceptions. Domain events raised by the aggregates are written to the outbox in that same transaction, and
/// handled afterwards by the <see cref="OutboxWorker"/>, so a failing handler can never undo or hide a save.
/// </summary>
internal sealed class UnitOfWork(ApplicationDbContext db, OutboxSignal outbox, TimeProvider time) : IUnitOfWork
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

        var now = time.GetUtcNow().UtcDateTime;
        var messages = aggregates.SelectMany(a => a.DomainEvents).Select(e => OutboxSerializer.ToMessage(e, now)).ToList();
        db.OutboxMessages.AddRange(messages);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            DetachOutbox(messages);
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
        {
            SqliteErrorCode: SqliteConstraint,
            SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey
        })
        {
            DetachOutbox(messages);
            throw new UniqueConstraintConflictException(ex);
        }

        aggregates.ForEach(a => a.ClearDomainEvents());
        if (messages.Count > 0)
        {
            outbox.Notify();
        }
    }

    // A failed save leaves the events on the aggregates; drop the rows so a retry doesn't write them twice.
    private void DetachOutbox(List<OutboxMessage> messages) =>
        messages.ForEach(m => db.Entry(m).State = EntityState.Detached);
}

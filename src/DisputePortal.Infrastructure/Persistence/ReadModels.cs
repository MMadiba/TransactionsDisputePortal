using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Contracts;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.Infrastructure.Persistence;

/// <summary>Read side for transactions: no-tracking projections straight into DTOs.</summary>
internal sealed class TransactionReadModel(ApplicationDbContext db) : ITransactionReadModel
{
    public async Task<PagedResult<TransactionSummary>> SearchAsync(TransactionSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var transactions = db.Transactions.AsNoTracking().Where(t => t.CustomerId == criteria.CustomerId);

        if (criteria.Search is { } search)
        {
            var pattern = $"%{EscapeLike(search)}%";
            transactions = transactions.Where(t =>
                EF.Functions.Like(t.Merchant, pattern, "\\") ||
                EF.Functions.Like(t.Description, pattern, "\\") ||
                EF.Functions.Like(t.Reference, pattern, "\\"));
        }

        if (criteria.FromUtc is { } fromUtc)
        {
            transactions = transactions.Where(t => t.PostedAtUtc >= fromUtc);
        }

        if (criteria.ToUtcExclusive is { } toUtc)
        {
            transactions = transactions.Where(t => t.PostedAtUtc < toUtc);
        }

        var total = await transactions.CountAsync(cancellationToken);
        var items = await transactions
            .OrderByDescending(t => t.PostedAtUtc)
            .ThenByDescending(t => t.Id)
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(t => new TransactionSummary(
                t.Id, t.Reference, t.PostedAtUtc, t.Merchant, t.Description,
                Major(t.Amount.AmountMinor), t.Amount.Currency, t.Status,
                db.Disputes
                    .Where(d => d.TransactionId == t.Id)
                    .OrderByDescending(d => d.CreatedAtUtc)
                    .Select(d => new DisputeLink(d.Id, d.Reference, d.Status))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PagedResult<TransactionSummary>(items, criteria.Page, criteria.PageSize, total);
    }

    internal static decimal Major(long minor) => minor / 100m;

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}

/// <summary>Read side for disputes: joins disputes, transactions and customer names in one query.</summary>
internal sealed class DisputeReadModel(ApplicationDbContext db) : IDisputeReadModel
{
    // Member-init (not a constructor) so EF Core can see through it when filtering and sorting.
    private sealed class Row
    {
        public required Dispute Dispute { get; init; }
        public required Transaction Transaction { get; init; }
        public required string CustomerName { get; init; }
    }

    private IQueryable<Row> Rows =>
        from d in db.Disputes.AsNoTracking()
        join t in db.Transactions on d.TransactionId equals t.Id
        join u in db.Users on d.CustomerId equals u.Id
        select new Row { Dispute = d, Transaction = t, CustomerName = u.FullName };

    public async Task<PagedResult<DisputeSummary>> SearchAsync(DisputeSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var rows = Rows;
        if (criteria.CustomerId is { } customerId)
        {
            rows = rows.Where(r => r.Dispute.CustomerId == customerId);
        }

        rows = criteria.Filter switch
        {
            DisputeFilter.Open => rows.Where(r => r.Dispute.Status == DisputeStatus.Submitted || r.Dispute.Status == DisputeStatus.UnderReview),
            DisputeFilter.Closed => rows.Where(r => r.Dispute.Status != DisputeStatus.Submitted && r.Dispute.Status != DisputeStatus.UnderReview),
            _ => rows
        };

        rows = criteria.Sort switch
        {
            DisputeSortOrder.OldestFirst => rows.OrderBy(r => r.Dispute.CreatedAtUtc),
            DisputeSortOrder.RecentlyUpdated => rows.OrderByDescending(r => r.Dispute.UpdatedAtUtc),
            _ => rows.OrderByDescending(r => r.Dispute.CreatedAtUtc)
        };

        var total = await rows.CountAsync(cancellationToken);
        var items = await ToSummaries(rows.Skip((criteria.Page - 1) * criteria.PageSize).Take(criteria.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<DisputeSummary>(items, criteria.Page, criteria.PageSize, total);
    }

    public async Task<IReadOnlyList<DisputeSummary>> ListForTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        await ToSummaries(Rows.Where(r => r.Dispute.TransactionId == transactionId).OrderByDescending(r => r.Dispute.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<DisputeDetail?> GetAsync(Guid disputeId, string? customerId, CancellationToken cancellationToken = default)
    {
        var disputes = db.Disputes.AsNoTracking().Include(d => d.History).Where(d => d.Id == disputeId);
        if (customerId is not null)
        {
            disputes = disputes.Where(d => d.CustomerId == customerId);
        }

        var dispute = await disputes.SingleOrDefaultAsync(cancellationToken);
        if (dispute is null)
        {
            return null;
        }

        var transaction = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == dispute.TransactionId, cancellationToken);
        var customerName = await db.Users.Where(u => u.Id == dispute.CustomerId).Select(u => u.FullName).SingleAsync(cancellationToken);

        return new DisputeDetail(
            dispute.Id, dispute.Reference, dispute.Status, dispute.Reason, dispute.Details, dispute.ResolutionNote, customerName,
            dispute.CreatedAtUtc, dispute.UpdatedAtUtc, dispute.ClosedAtUtc,
            new TransactionSummary(transaction.Id, transaction.Reference, transaction.PostedAtUtc, transaction.Merchant,
                transaction.Description, transaction.Amount.Amount, transaction.Amount.Currency, transaction.Status,
                new DisputeLink(dispute.Id, dispute.Reference, dispute.Status)),
            dispute.History
                .OrderBy(h => h.OccurredAtUtc)
                .Select(h => new DisputeHistoryItem(h.Status, h.ActorRole, h.ActorName, h.Note, h.OccurredAtUtc))
                .ToList(),
            dispute.IsOpen,
            dispute.ReviewerId,
            dispute.ReviewerName);
    }

    private static IQueryable<DisputeSummary> ToSummaries(IQueryable<Row> rows) =>
        rows.Select(r => new DisputeSummary(
            r.Dispute.Id,
            r.Dispute.Reference,
            r.Dispute.TransactionId,
            r.Transaction.Reference,
            r.Transaction.Merchant,
            TransactionReadModel.Major(r.Transaction.Amount.AmountMinor),
            r.Transaction.Amount.Currency,
            r.Dispute.Reason,
            r.Dispute.Status,
            r.CustomerName,
            r.Dispute.CreatedAtUtc,
            r.Dispute.UpdatedAtUtc,
            r.Dispute.ClosedAtUtc));
}

using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.UnitTests.Application;

// Hand-written fakes for the application's ports. Because handlers only depend on interfaces,
// the use cases can be tested in milliseconds with no database.

internal sealed class FakeTransactionRepository : ITransactionRepository
{
    public List<Transaction> Items { get; } = [];

    public Task<Transaction?> GetOwnedByAsync(Guid transactionId, string customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(t => t.Id == transactionId && t.CustomerId == customerId));
}

internal sealed class FakeDisputeRepository : IDisputeRepository
{
    public List<Dispute> Items { get; } = [];

    public Task<Dispute?> GetByIdAsync(Guid disputeId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(d => d.Id == disputeId));

    public Task<Dispute?> GetOwnedByAsync(Guid disputeId, string customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(d => d.Id == disputeId && d.CustomerId == customerId));

    public Task<Dispute?> FindLiveForTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(d => d.TransactionId == transactionId && d.IsLive));

    public void Add(Dispute dispute) => Items.Add(dispute);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }
    public Exception? ThrowOnSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave is not null)
        {
            throw ThrowOnSave;
        }

        Saves++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    public Dictionary<string, string> Names { get; } = new() { ["customer-1"] = "Casey Customer", ["agent-1"] = "Avery Agent" };

    public Task<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Names.GetValueOrDefault(userId));
}

/// <summary>Serves details straight from the fake repository's aggregates.</summary>
internal sealed class FakeDisputeReadModel(FakeDisputeRepository disputes, FakeTransactionRepository transactions) : IDisputeReadModel
{
    public Task<PagedResult<DisputeSummary>> SearchAsync(DisputeSearchCriteria criteria, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<DisputeSummary>> ListForTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DisputeSummary>>([]);

    public Task<DisputeDetail?> GetAsync(Guid disputeId, string? customerId, CancellationToken cancellationToken = default)
    {
        var d = disputes.Items.SingleOrDefault(x => x.Id == disputeId && (customerId is null || x.CustomerId == customerId));
        if (d is null)
        {
            return Task.FromResult<DisputeDetail?>(null);
        }

        var t = transactions.Items.Single(x => x.Id == d.TransactionId);
        return Task.FromResult<DisputeDetail?>(new DisputeDetail(
            d.Id, d.Reference, d.Status, d.Reason, d.Details, d.ResolutionNote, "Casey Customer",
            d.CreatedAtUtc, d.UpdatedAtUtc, d.ClosedAtUtc,
            new TransactionSummary(t.Id, t.Reference, t.PostedAtUtc, t.Merchant, t.Description, t.Amount.Amount, t.Amount.Currency, t.Status, null),
            d.History.Select(h => new DisputeHistoryItem(h.Status, h.ActorRole, h.ActorName, h.Note, h.OccurredAtUtc)).ToList(),
            d.IsOpen));
    }
}

internal sealed class CapturingTransactionReadModel : ITransactionReadModel
{
    public TransactionSearchCriteria? LastCriteria { get; private set; }

    public Task<PagedResult<TransactionSummary>> SearchAsync(TransactionSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        LastCriteria = criteria;
        return Task.FromResult(new PagedResult<TransactionSummary>([], criteria.Page, criteria.PageSize, 0));
    }
}

internal sealed class FixedClock(DateTime utcNow) : IPortalClock
{
    public DateTime UtcNow { get; } = utcNow;
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    public DateOnly Today => DateOnly.FromDateTime(ToLocal(UtcNow));
    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    public DateTime StartOfDayUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Zone);
}

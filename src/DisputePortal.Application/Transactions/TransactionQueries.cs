using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Application.Transactions;

// ---------- List ----------

public sealed record ListTransactionsQuery(
    string CustomerId, string? Search = null, DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 20);

internal sealed class ListTransactionsHandler(ITransactionReadModel transactions, IPortalClock clock)
    : IQueryHandler<ListTransactionsQuery, PagedResult<TransactionSummary>>
{
    public Task<PagedResult<TransactionSummary>> HandleAsync(ListTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = RequestValidator.NormalisePaging(query.Page, query.PageSize);

        // Date filters are whole days in the customer's time zone, turned into a half-open UTC range here.
        var criteria = new TransactionSearchCriteria(
            query.CustomerId,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            query.From is { } from ? clock.StartOfDayUtc(from) : null,
            query.To is { } to ? clock.StartOfDayUtc(to.AddDays(1)) : null,
            page,
            pageSize);

        return transactions.SearchAsync(criteria, cancellationToken);
    }
}

// ---------- Detail ----------

public sealed record GetTransactionQuery(string CustomerId, Guid TransactionId);

/// <summary>
/// Loads the aggregate (not just a projection) because it asks the domain policy whether the
/// transaction can be disputed. That way the page explains exactly the rule that would be enforced.
/// </summary>
internal sealed class GetTransactionHandler(
    ITransactionRepository transactions,
    IDisputeRepository disputes,
    IDisputeReadModel disputeReadModel,
    DisputeEligibilityPolicy policy,
    IPortalClock clock) : IQueryHandler<GetTransactionQuery, TransactionDetail>
{
    public async Task<TransactionDetail> HandleAsync(GetTransactionQuery query, CancellationToken cancellationToken = default)
    {
        var transaction = await transactions.GetOwnedByAsync(query.TransactionId, query.CustomerId, cancellationToken)
            ?? throw Errors.TransactionNotFound();

        var blocking = await disputes.FindBlockingForTransactionAsync(transaction.Id, cancellationToken);
        var eligibility = policy.Evaluate(transaction, blocking, clock.UtcNow);
        var history = await disputeReadModel.ListForTransactionAsync(transaction.Id, cancellationToken);

        return new TransactionDetail(
            transaction.Id, transaction.Reference, transaction.PostedAtUtc, transaction.Merchant, transaction.Description,
            transaction.Amount.Amount, transaction.Amount.Currency, transaction.Status, transaction.CardLast4,
            eligibility.CanDispute, eligibility.Message, history);
    }
}

internal static class Errors
{
    public static NotFoundException TransactionNotFound() =>
        new("transaction.not_found", "We couldn't find that transaction on your account.");

    public static NotFoundException DisputeNotFound() =>
        new("dispute.not_found", "We couldn't find that dispute.");
}

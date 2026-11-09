using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Transactions;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Application.Disputes;

// ---------- Open ----------

public sealed record OpenDisputeCommand(string CustomerId, Guid TransactionId, OpenDisputeRequest Request);

internal sealed class OpenDisputeHandler(
    ITransactionRepository transactions,
    IDisputeRepository disputes,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    IDisputeReadModel readModel,
    DisputeEligibilityPolicy policy,
    IPortalClock clock) : ICommandHandler<OpenDisputeCommand, DisputeDetail>
{
    public async Task<DisputeDetail> HandleAsync(OpenDisputeCommand command, CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(command.Request);

        var transaction = await transactions.GetOwnedByAsync(command.TransactionId, command.CustomerId, cancellationToken)
            ?? throw Errors.TransactionNotFound();
        var live = await disputes.FindLiveForTransactionAsync(transaction.Id, cancellationToken);
        var customer = await Actors.CustomerAsync(users, command.CustomerId, cancellationToken);

        // All the rules live in the aggregate; the handler only orchestrates.
        var dispute = Dispute.Open(transaction, live, policy, command.Request.Reason!.Value, command.Request.Details, customer, clock.UtcNow);
        disputes.Add(dispute);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            // Lost a race with a concurrent request for the same transaction: the database caught it.
            throw new DomainConflictException("dispute.already_exists", "This transaction already has a dispute.");
        }

        return await DisputeViews.GetForCustomerAsync(readModel, dispute.Id, command.CustomerId, cancellationToken);
    }
}

// ---------- Withdraw ----------

public sealed record WithdrawDisputeCommand(string CustomerId, Guid DisputeId);

internal sealed class WithdrawDisputeHandler(
    IDisputeRepository disputes,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    IDisputeReadModel readModel,
    IPortalClock clock) : ICommandHandler<WithdrawDisputeCommand, DisputeDetail>
{
    public async Task<DisputeDetail> HandleAsync(WithdrawDisputeCommand command, CancellationToken cancellationToken = default)
    {
        var dispute = await disputes.GetOwnedByAsync(command.DisputeId, command.CustomerId, cancellationToken)
            ?? throw Errors.DisputeNotFound();
        var customer = await Actors.CustomerAsync(users, command.CustomerId, cancellationToken);

        dispute.Withdraw(customer, clock.UtcNow);
        await unitOfWork.SaveWithConcurrencyCheckAsync(cancellationToken);

        return await DisputeViews.GetForCustomerAsync(readModel, dispute.Id, command.CustomerId, cancellationToken);
    }
}

// ---------- History ----------

public sealed record ListMyDisputesQuery(string CustomerId, DisputeFilter Filter = DisputeFilter.All, int Page = 1, int PageSize = 20);

internal sealed class ListMyDisputesHandler(IDisputeReadModel readModel) : IQueryHandler<ListMyDisputesQuery, PagedResult<DisputeSummary>>
{
    public Task<PagedResult<DisputeSummary>> HandleAsync(ListMyDisputesQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = RequestValidator.NormalisePaging(query.Page, query.PageSize);
        return readModel.SearchAsync(new DisputeSearchCriteria(query.CustomerId, query.Filter, DisputeSortOrder.NewestFirst, page, pageSize), cancellationToken);
    }
}

public sealed record GetMyDisputeQuery(string CustomerId, Guid DisputeId);

internal sealed class GetMyDisputeHandler(IDisputeReadModel readModel) : IQueryHandler<GetMyDisputeQuery, DisputeDetail>
{
    public Task<DisputeDetail> HandleAsync(GetMyDisputeQuery query, CancellationToken cancellationToken = default) =>
        DisputeViews.GetForCustomerAsync(readModel, query.DisputeId, query.CustomerId, cancellationToken);
}

// ---------- Shared helpers ----------

internal static class Actors
{
    public static async Task<Actor> CustomerAsync(IUserDirectory users, string userId, CancellationToken cancellationToken) =>
        Actor.Customer(userId, await users.GetDisplayNameAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} is signed in but does not exist."));

    public static async Task<Actor> AgentAsync(IUserDirectory users, string userId, CancellationToken cancellationToken) =>
        Actor.Agent(userId, await users.GetDisplayNameAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} is signed in but does not exist."));
}

internal static class UnitOfWorkExtensions
{
    public static async Task SaveWithConcurrencyCheckAsync(this IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            throw new DomainConflictException("dispute.concurrent_update",
                "Someone else updated this dispute at the same time. Please refresh and try again.");
        }
    }
}

/// <summary>Shapes dispute details for each audience. Customers never see individual agents' names.</summary>
internal static class DisputeViews
{
    public static async Task<DisputeDetail> GetForCustomerAsync(IDisputeReadModel readModel, Guid disputeId, string customerId, CancellationToken cancellationToken)
    {
        var detail = await readModel.GetAsync(disputeId, customerId, cancellationToken) ?? throw Errors.DisputeNotFound();
        return detail with
        {
            Timeline = detail.Timeline.Select(e => e with
            {
                Actor = e.ActorRole switch
                {
                    ActorRole.Customer => "You",
                    ActorRole.Agent => "Disputes team",
                    _ => "System"
                }
            }).ToList()
        };
    }

    public static async Task<DisputeDetail> GetForAgentAsync(IDisputeReadModel readModel, Guid disputeId, CancellationToken cancellationToken) =>
        await readModel.GetAsync(disputeId, customerId: null, cancellationToken) ?? throw Errors.DisputeNotFound();
}

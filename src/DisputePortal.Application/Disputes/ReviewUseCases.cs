using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Transactions;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Application.Disputes;

// Back-office use cases for the disputes team.

public sealed record ListDisputeQueueQuery(DisputeFilter Filter = DisputeFilter.Open, int Page = 1, int PageSize = 20);

internal sealed class ListDisputeQueueHandler(IDisputeReadModel readModel) : IQueryHandler<ListDisputeQueueQuery, PagedResult<DisputeSummary>>
{
    public Task<PagedResult<DisputeSummary>> HandleAsync(ListDisputeQueueQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = RequestValidator.NormalisePaging(query.Page, query.PageSize);

        // The open queue is worked oldest-first; everything else reads best by most recent activity.
        var sort = query.Filter == DisputeFilter.Open ? DisputeSortOrder.OldestFirst : DisputeSortOrder.RecentlyUpdated;
        return readModel.SearchAsync(new DisputeSearchCriteria(null, query.Filter, sort, page, pageSize), cancellationToken);
    }
}

public sealed record GetDisputeForReviewQuery(Guid DisputeId);

internal sealed class GetDisputeForReviewHandler(IDisputeReadModel readModel) : IQueryHandler<GetDisputeForReviewQuery, DisputeDetail>
{
    public Task<DisputeDetail> HandleAsync(GetDisputeForReviewQuery query, CancellationToken cancellationToken = default) =>
        DisputeViews.GetForAgentAsync(readModel, query.DisputeId, cancellationToken);
}

public sealed record StartReviewCommand(string AgentId, Guid DisputeId);

internal sealed class StartReviewHandler(
    IDisputeRepository disputes,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    IDisputeReadModel readModel,
    IPortalClock clock) : ICommandHandler<StartReviewCommand, DisputeDetail>
{
    public async Task<DisputeDetail> HandleAsync(StartReviewCommand command, CancellationToken cancellationToken = default)
    {
        var dispute = await disputes.GetByIdAsync(command.DisputeId, cancellationToken) ?? throw Errors.DisputeNotFound();
        var agent = await Actors.AgentAsync(users, command.AgentId, cancellationToken);

        dispute.StartReview(agent, clock.UtcNow);
        await unitOfWork.SaveWithConcurrencyCheckAsync(cancellationToken);

        return await DisputeViews.GetForAgentAsync(readModel, dispute.Id, cancellationToken);
    }
}

public sealed record ResolveDisputeCommand(string AgentId, Guid DisputeId, ResolveDisputeRequest Request);

internal sealed class ResolveDisputeHandler(
    IDisputeRepository disputes,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    IDisputeReadModel readModel,
    IPortalClock clock) : ICommandHandler<ResolveDisputeCommand, DisputeDetail>
{
    public async Task<DisputeDetail> HandleAsync(ResolveDisputeCommand command, CancellationToken cancellationToken = default)
    {
        RequestValidator.Validate(command.Request);

        var dispute = await disputes.GetByIdAsync(command.DisputeId, cancellationToken) ?? throw Errors.DisputeNotFound();
        var agent = await Actors.AgentAsync(users, command.AgentId, cancellationToken);

        dispute.Resolve(command.Request.Outcome!.Value, command.Request.Note, agent, clock.UtcNow);
        await unitOfWork.SaveWithConcurrencyCheckAsync(cancellationToken);

        return await DisputeViews.GetForAgentAsync(readModel, dispute.Id, cancellationToken);
    }
}

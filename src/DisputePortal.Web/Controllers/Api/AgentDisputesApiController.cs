using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Web.Controllers.Api;

[Route("api/agent/disputes")]
[Tags("Disputes team")]
[Authorize(Policy = Policies.ApiAgent)]
public sealed class AgentDisputesApiController : ApiControllerBase
{
    /// <summary>The disputes queue. Open disputes come back oldest first.</summary>
    [HttpGet]
    public Task<PagedResult<DisputeSummary>> List(
        [FromServices] IQueryHandler<ListDisputeQueueQuery, PagedResult<DisputeSummary>> handler,
        [FromQuery] DisputeFilter filter = DisputeFilter.Open, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        handler.HandleAsync(new ListDisputeQueueQuery(filter, page, pageSize), ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<DisputeDetail> Get(Guid id, [FromServices] IQueryHandler<GetDisputeForReviewQuery, DisputeDetail> handler, CancellationToken ct) =>
        handler.HandleAsync(new GetDisputeForReviewQuery(id), ct);

    /// <summary>Pick up a submitted dispute.</summary>
    [HttpPost("{id:guid}/start-review")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DisputeDetail> StartReview(Guid id, [FromServices] ICommandHandler<StartReviewCommand, DisputeDetail> handler, CancellationToken ct) =>
        handler.HandleAsync(new StartReviewCommand(UserId, id), ct);

    /// <summary>Close a dispute as upheld (customer refunded) or declined.</summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DisputeDetail> Resolve(
        Guid id, ResolveDisputeRequest request,
        [FromServices] ICommandHandler<ResolveDisputeCommand, DisputeDetail> handler, CancellationToken ct) =>
        handler.HandleAsync(new ResolveDisputeCommand(UserId, id, request), ct);
}

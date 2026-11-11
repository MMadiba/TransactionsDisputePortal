using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Application.Transactions;
using DisputePortal.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Api.Controllers;

[Route("api/transactions")]
[Tags("Transactions")]
[Authorize(Policy = Policies.Customer)]
public sealed class TransactionsApiController : ApiControllerBase
{
    /// <summary>List the caller's transactions, newest first.</summary>
    [HttpGet]
    public Task<PagedResult<TransactionSummary>> List(
        [FromServices] IQueryHandler<ListTransactionsQuery, PagedResult<TransactionSummary>> handler,
        [FromQuery] string? search, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        handler.HandleAsync(new ListTransactionsQuery(UserId, search, from, to, page, pageSize), ct);

    /// <summary>One transaction, whether it can be disputed (and why not), and its disputes.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<TransactionDetail> Get(Guid id, [FromServices] IQueryHandler<GetTransactionQuery, TransactionDetail> handler, CancellationToken ct) =>
        handler.HandleAsync(new GetTransactionQuery(UserId, id), ct);

    /// <summary>Raise a dispute against a transaction.</summary>
    [HttpPost("{id:guid}/disputes")]
    [EnableRateLimiting(RateLimits.DisputeWrites)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DisputeDetail>> OpenDispute(
        Guid id, OpenDisputeRequest request,
        [FromServices] ICommandHandler<OpenDisputeCommand, DisputeDetail> handler, CancellationToken ct)
    {
        var dispute = await handler.HandleAsync(new OpenDisputeCommand(UserId, id, request), ct);
        return CreatedAtAction(nameof(DisputesApiController.Get), "DisputesApi", new { id = dispute.Id }, dispute);
    }
}

[Route("api/disputes")]
[Tags("Disputes")]
[Authorize(Policy = Policies.Customer)]
public sealed class DisputesApiController : ApiControllerBase
{
    /// <summary>The caller's dispute history (open and closed), newest first.</summary>
    [HttpGet]
    public Task<PagedResult<DisputeSummary>> List(
        [FromServices] IQueryHandler<ListMyDisputesQuery, PagedResult<DisputeSummary>> handler,
        [FromQuery] DisputeFilter filter = DisputeFilter.All, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        handler.HandleAsync(new ListMyDisputesQuery(UserId, filter, page, pageSize), ct);

    /// <summary>One dispute with its full timeline.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<DisputeDetail> Get(Guid id, [FromServices] IQueryHandler<GetMyDisputeQuery, DisputeDetail> handler, CancellationToken ct) =>
        handler.HandleAsync(new GetMyDisputeQuery(UserId, id), ct);

    /// <summary>Withdraw an open dispute.</summary>
    [HttpPost("{id:guid}/withdraw")]
    [EnableRateLimiting(RateLimits.DisputeWrites)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<DisputeDetail> Withdraw(Guid id, [FromServices] ICommandHandler<WithdrawDisputeCommand, DisputeDetail> handler, CancellationToken ct) =>
        handler.HandleAsync(new WithdrawDisputeCommand(UserId, id), ct);
}

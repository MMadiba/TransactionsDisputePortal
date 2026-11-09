using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Domain.Common;
using DisputePortal.Web.Models;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Web.Controllers;

[Authorize(Policy = Policies.Customer)]
[Route("disputes")]
public sealed class DisputesController(IQueryHandler<GetMyDisputeQuery, DisputeDetail> getDispute) : PortalController
{
    /// <summary>The customer's dispute history: everything they've ever raised, open or closed.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromServices] IQueryHandler<ListMyDisputesQuery, PagedResult<DisputeSummary>> listDisputes,
        DisputeFilter filter = DisputeFilter.All, int page = 1, CancellationToken ct = default) =>
        View(new DisputesIndexViewModel(await listDisputes.HandleAsync(new ListMyDisputesQuery(UserId, filter, page), ct), filter));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct) =>
        View(new DisputeDetailsViewModel(await getDispute.HandleAsync(new GetMyDisputeQuery(UserId, id), ct)));

    [HttpPost("{id:guid}/withdraw")]
    [EnableRateLimiting(RateLimits.DisputeWrites)]
    public async Task<IActionResult> Withdraw(
        Guid id, [FromServices] ICommandHandler<WithdrawDisputeCommand, DisputeDetail> withdraw, CancellationToken ct)
    {
        try
        {
            var dispute = await withdraw.HandleAsync(new WithdrawDisputeCommand(UserId, id), ct);
            Flash($"Dispute {dispute.Reference} has been withdrawn.");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (DomainException ex) when (ex is not NotFoundException)
        {
            AddError(ex);
            return View(nameof(Details), new DisputeDetailsViewModel(await getDispute.HandleAsync(new GetMyDisputeQuery(UserId, id), ct)));
        }
    }
}

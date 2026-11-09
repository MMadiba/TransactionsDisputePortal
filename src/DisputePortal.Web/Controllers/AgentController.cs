using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Domain.Common;
using DisputePortal.Web.Models;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Web.Controllers;

/// <summary>The disputes team's back office: work the queue, review, resolve.</summary>
[Authorize(Policy = Policies.Agent)]
[Route("agent/disputes")]
public sealed class AgentController(IQueryHandler<GetDisputeForReviewQuery, DisputeDetail> getDispute) : PortalController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromServices] IQueryHandler<ListDisputeQueueQuery, PagedResult<DisputeSummary>> listQueue,
        DisputeFilter filter = DisputeFilter.Open, int page = 1, CancellationToken ct = default) =>
        View(new DisputesIndexViewModel(await listQueue.HandleAsync(new ListDisputeQueueQuery(filter, page), ct), filter));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct) =>
        View(new DisputeDetailsViewModel(await getDispute.HandleAsync(new GetDisputeForReviewQuery(id), ct), new ResolveDisputeRequest()));

    [HttpPost("{id:guid}/start-review")]
    public async Task<IActionResult> StartReview(
        Guid id, [FromServices] ICommandHandler<StartReviewCommand, DisputeDetail> startReview, CancellationToken ct)
    {
        try
        {
            var dispute = await startReview.HandleAsync(new StartReviewCommand(UserId, id), ct);
            Flash($"You've picked up {dispute.Reference}.");
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (DomainException ex) when (ex is not NotFoundException)
        {
            AddError(ex);
            return View(nameof(Details), new DisputeDetailsViewModel(await getDispute.HandleAsync(new GetDisputeForReviewQuery(id), ct), new ResolveDisputeRequest()));
        }
    }

    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(
        Guid id,
        [Bind(Prefix = "ResolveForm")] ResolveDisputeRequest form,
        [FromServices] ICommandHandler<ResolveDisputeCommand, DisputeDetail> resolve,
        CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var dispute = await resolve.HandleAsync(new ResolveDisputeCommand(UserId, id, form), ct);
                Flash($"{dispute.Reference} closed as {dispute.Status.ToString().ToLowerInvariant()}.");
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (DomainException ex) when (ex is not NotFoundException)
            {
                AddError(ex);
            }
        }

        return View(nameof(Details), new DisputeDetailsViewModel(await getDispute.HandleAsync(new GetDisputeForReviewQuery(id), ct), form));
    }
}

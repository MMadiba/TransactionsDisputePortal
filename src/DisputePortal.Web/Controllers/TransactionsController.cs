using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Application.Transactions;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Web.Models;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Web.Controllers;

[Authorize(Policy = Policies.Customer)]
[Route("transactions")]
public sealed class TransactionsController(
    IQueryHandler<GetTransactionQuery, TransactionDetail> getTransaction,
    DisputeEligibilityPolicy policy) : PortalController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search, DateOnly? from, DateOnly? to,
        [FromServices] IQueryHandler<ListTransactionsQuery, PagedResult<TransactionSummary>> listTransactions,
        int page = 1, CancellationToken ct = default)
    {
        var result = await listTransactions.HandleAsync(new ListTransactionsQuery(UserId, search, from, to, page), ct);
        return View(new TransactionsIndexViewModel(result, search, from, to, policy.WindowDays));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct) =>
        View(new TransactionDetailsViewModel(await getTransaction.HandleAsync(new GetTransactionQuery(UserId, id), ct), new OpenDisputeRequest()));

    [HttpPost("{id:guid}/dispute")]
    [EnableRateLimiting(RateLimits.DisputeWrites)]
    public async Task<IActionResult> Dispute(
        Guid id,
        [Bind(Prefix = "Form")] OpenDisputeRequest form,
        [FromServices] ICommandHandler<OpenDisputeCommand, DisputeDetail> openDispute,
        CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var dispute = await openDispute.HandleAsync(new OpenDisputeCommand(UserId, id, form), ct);
                Flash($"Thanks — we've received your dispute. Your reference is {dispute.Reference}.");
                return RedirectToAction(nameof(DisputesController.Details), "Disputes", new { id = dispute.Id });
            }
            catch (DomainException ex) when (ex is not NotFoundException)
            {
                AddError(ex);
            }
        }

        return View(nameof(Details), new TransactionDetailsViewModel(await getTransaction.HandleAsync(new GetTransactionQuery(UserId, id), ct), form));
    }
}

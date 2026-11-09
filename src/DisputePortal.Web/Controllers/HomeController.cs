using System.Diagnostics;
using DisputePortal.Application.Common;
using DisputePortal.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Web.Controllers;

public sealed class HomeController : PortalController
{
    // There's no start page: "/" sends each user to where they work, and everyone else to sign in.
    public IActionResult Index()
    {
        if (User.IsInRole(Roles.Agent))
        {
            return RedirectToAction(nameof(AgentController.Index), "Agent");
        }

        if (User.IsInRole(Roles.Customer))
        {
            return RedirectToAction(nameof(TransactionsController.Index), "Transactions");
        }

        return RedirectToPage("/Account/Login", new { area = "Identity" });
    }

    // Error and status pages are re-executed with the ORIGINAL request's method, so a failed POST arrives
    // here as a POST. They must not demand an antiforgery token or every error would become a 400.
    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    [IgnoreAntiforgeryToken]
    [Route("Home/Status/{code:int}")]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code;
        return View(code);
    }
}

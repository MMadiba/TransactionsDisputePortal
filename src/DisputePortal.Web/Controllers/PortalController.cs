using DisputePortal.Application.Common;
using DisputePortal.Domain.Common;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DisputePortal.Web.Controllers;

/// <summary>
/// Base for the server-rendered pages. Kept out of the API description; a "not found" from a use case
/// becomes the friendly 404 page instead of an error page.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public abstract class PortalController : Controller
{
    protected string UserId => User.GetUserId();

    protected void Flash(string message) => TempData["Flash"] = message;

    /// <summary>Shows an expected failure (rule broken, already closed…) on the form instead of crashing.</summary>
    protected void AddError(DomainException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is RequestValidationException validation)
        {
            foreach (var (field, messages) in validation.Errors)
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(field, message);
                }
            }

            return;
        }

        ModelState.AddModelError(string.Empty, exception.Message);
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Exception is NotFoundException && !context.ExceptionHandled)
        {
            context.Result = NotFound();
            context.ExceptionHandled = true;
        }

        base.OnActionExecuted(context);
    }
}

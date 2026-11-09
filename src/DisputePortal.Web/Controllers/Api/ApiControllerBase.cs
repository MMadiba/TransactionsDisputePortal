using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Web.Controllers.Api;

/// <summary>
/// Base for the JSON API. JWT bearer only (no cookies), so it isn't exposed to CSRF and antiforgery
/// doesn't apply. Failures become problem details via the API exception handler.
/// </summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected string UserId => User.GetUserId();
}

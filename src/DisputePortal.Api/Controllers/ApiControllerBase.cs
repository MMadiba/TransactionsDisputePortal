using DisputePortal.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.Api.Controllers;

/// <summary>
/// Base for the JSON API. Requests authenticate with a JWT bearer token, never a cookie, so they can't be
/// forged cross-site. Failures become problem details via the API exception handler.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected string UserId => User.GetUserId();
}

using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Contracts;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Web.Controllers.Api;

[Route("api/auth")]
[Tags("Authentication")]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Authentication)]
public sealed class AuthApiController(IAuthTokenService tokens) : ApiControllerBase
{
    /// <summary>Exchange an email and password for a JWT access token and a refresh token.</summary>
    [HttpPost("token")]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Token(AccessTokenRequest request, CancellationToken ct) =>
        ToResponse(await tokens.IssueAsync(request.Email, request.Password, ct));

    /// <summary>Swap a refresh token for a new pair. Each refresh token works once (rotation).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken ct) =>
        ToResponse(await tokens.RefreshAsync(request.RefreshToken, ct));

    /// <summary>Sign out: revoke a refresh token. Always succeeds so it can't be used to test tokens.</summary>
    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(RefreshTokenRequest request, CancellationToken ct)
    {
        await tokens.RevokeAsync(request.RefreshToken, ct);
        return NoContent();
    }

    private IActionResult ToResponse(AuthTokenResult result) => result switch
    {
        { Tokens: { } issued } => Ok(issued),
        { Failure: AuthFailure.LockedOut } => Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "Account locked", detail: "Too many failed attempts. Try again in a few minutes."),
        { Failure: AuthFailure.InvalidRefreshToken } => Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "Session expired", detail: "The refresh token is invalid or expired. Please sign in again."),
        _ => Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "Sign-in failed", detail: "The email or password is incorrect.")
    };
}

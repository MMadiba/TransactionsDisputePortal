using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Contracts;
using DisputePortal.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace DisputePortal.Api.Controllers;

/// <summary>
/// Sign-in, refresh and sign-out. API clients get the refresh token in the response body. Browsers ask for
/// it as a cookie instead (<see cref="AccessTokenRequest.UseCookie"/>): HttpOnly so scripts can't read it,
/// SameSite=Strict so other sites can't use it, and scoped to /api/auth so it isn't sent anywhere else.
/// </summary>
[Route("api/auth")]
[Tags("Authentication")]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Authentication)]
public sealed class AuthApiController(IAuthTokenService tokens) : ApiControllerBase
{
    public const string RefreshCookie = "dp_refresh";
    private const string CookiePath = "/api/auth";

    /// <summary>Exchange an email and password for a JWT access token and a refresh token.</summary>
    [HttpPost("token")]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Token(AccessTokenRequest request, CancellationToken ct) =>
        ToResponse(await tokens.IssueAsync(request.Email, request.Password, ct), request.UseCookie);

    /// <summary>
    /// Swap a refresh token for a new pair. Each refresh token works once (rotation). Send it in the body,
    /// or send no body to use the refresh-token cookie.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request, CancellationToken ct)
    {
        var (refreshToken, fromCookie) = ResolveRefreshToken(request);
        if (refreshToken is null)
        {
            return SessionExpired();
        }

        return ToResponse(await tokens.RefreshAsync(refreshToken, ct), fromCookie);
    }

    /// <summary>Sign out: revoke a refresh token (from the body or the cookie). Always succeeds so it can't be used to test tokens.</summary>
    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request, CancellationToken ct)
    {
        var (refreshToken, _) = ResolveRefreshToken(request);
        if (refreshToken is not null)
        {
            await tokens.RevokeAsync(refreshToken, ct);
        }

        ClearRefreshCookie();
        return NoContent();
    }

    private (string? Token, bool FromCookie) ResolveRefreshToken(RefreshTokenRequest? request) =>
        !string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? (request.RefreshToken, false)
            : (Request.Cookies[RefreshCookie] is { Length: > 0 } cookie ? cookie : null, true);

    private IActionResult ToResponse(AuthTokenResult result, bool useCookie)
    {
        if (result.Tokens is { } issued)
        {
            if (!useCookie)
            {
                return Ok(issued);
            }

            Response.Cookies.Append(RefreshCookie, issued.RefreshToken!, CookieOptions(issued.RefreshTokenExpiresAtUtc));
            return Ok(issued with { RefreshToken = null });
        }

        if (useCookie)
        {
            ClearRefreshCookie();
        }

        return result.Failure switch
        {
            AuthFailure.LockedOut => Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Account locked", detail: "Too many failed attempts. Try again in a few minutes."),
            AuthFailure.InvalidRefreshToken => SessionExpired(),
            _ => Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in failed", detail: "The email or password is incorrect.")
        };
    }

    private ObjectResult SessionExpired() => Problem(statusCode: StatusCodes.Status401Unauthorized,
        title: "Session expired", detail: "The refresh token is invalid or expired. Please sign in again.");

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookie, CookieOptions(expiresAtUtc: null));

    // Secure is always on: browsers accept Secure cookies on http://localhost, and anywhere else the portal
    // must be served over HTTPS (TLS usually ends at a proxy; see ASPNETCORE_FORWARDEDHEADERS_ENABLED).
    private static CookieOptions CookieOptions(DateTime? expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
        Expires = expiresAtUtc is { } expires ? new DateTimeOffset(expires, TimeSpan.Zero) : null,
        IsEssential = true
    };
}

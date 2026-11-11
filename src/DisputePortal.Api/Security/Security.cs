using System.Security.Claims;
using DisputePortal.Infrastructure.Authentication;

namespace DisputePortal.Api.Security;

public static class Policies
{
    /// <summary>A signed-in customer (JWT).</summary>
    public const string Customer = nameof(Customer);

    /// <summary>A signed-in member of the disputes team (JWT).</summary>
    public const string Agent = nameof(Agent);
}

public static class RateLimits
{
    public const string DisputeWrites = "dispute-writes";
    public const string Authentication = "authentication";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The user id, from the access token's "sub" claim.</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindUserId() ?? throw new InvalidOperationException("The current user has no identifier claim.");

    public static string? FindUserId(this ClaimsPrincipal user) => user.FindFirstValue(PortalClaims.Subject);
}

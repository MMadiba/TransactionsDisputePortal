using System.Security.Claims;
using DisputePortal.Infrastructure.Authentication;

namespace DisputePortal.Web.Security;

public static class Policies
{
    /// <summary>Signed-in customer using the website (cookie).</summary>
    public const string Customer = nameof(Customer);

    /// <summary>Signed-in disputes agent using the website (cookie).</summary>
    public const string Agent = nameof(Agent);

    /// <summary>Customer calling the REST API with a JWT.</summary>
    public const string ApiCustomer = nameof(ApiCustomer);

    /// <summary>Agent calling the REST API with a JWT.</summary>
    public const string ApiAgent = nameof(ApiAgent);
}

public static class RateLimits
{
    public const string DisputeWrites = "dispute-writes";
    public const string Authentication = "authentication";
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The user id from either a cookie principal (NameIdentifier) or a JWT principal (sub).</summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(PortalClaims.Subject)
        ?? throw new InvalidOperationException("The current user has no identifier claim.");

    public static string? FindUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(PortalClaims.Subject);
}

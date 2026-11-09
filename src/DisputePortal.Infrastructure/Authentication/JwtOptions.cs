using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "dispute-portal";

    [Required]
    public string Audience { get; set; } = "dispute-portal-api";

    /// <summary>HMAC-SHA256 signing secret. At least 32 characters (256 bits); supply it from a secret store, never source control.</summary>
    [Required(ErrorMessage = "Jwt:SigningKey must be configured.")]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access tokens are short-lived; clients use the refresh token to get a new one.</summary>
    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

/// <summary>Claim names used in our JWTs (short registered names, not the long WS-* URIs).</summary>
public static class PortalClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string TokenId = "jti";
}

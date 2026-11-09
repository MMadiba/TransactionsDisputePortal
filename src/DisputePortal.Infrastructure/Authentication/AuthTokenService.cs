using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Contracts;
using DisputePortal.Infrastructure.Identity;
using DisputePortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.Infrastructure.Authentication;

/// <summary>
/// A refresh token. Only a SHA-256 hash is stored, so a database leak doesn't hand out working tokens.
/// Tokens rotate on every use; presenting an already-used token revokes the whole family (theft detection).
/// </summary>
internal sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string UserId { get; init; }
    public required string TokenHash { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}

internal sealed partial class AuthTokenService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    IOptions<JwtOptions> options,
    TimeProvider time,
    ILogger<AuthTokenService> logger) : IAuthTokenService
{
    private static readonly JsonWebTokenHandler TokenHandler = new();
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokenResult> IssueAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return AuthTokenResult.Fail(AuthFailure.InvalidCredentials);
        }

        // Checks the password, honours lockout and counts failures, without issuing a cookie.
        var check = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            return AuthTokenResult.Fail(AuthFailure.LockedOut);
        }

        if (!check.Succeeded)
        {
            return AuthTokenResult.Fail(AuthFailure.InvalidCredentials);
        }

        return AuthTokenResult.Success(await IssueTokensAsync(user, previous: null, cancellationToken));
    }

    public async Task<AuthTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var stored = await FindAsync(refreshToken, cancellationToken);
        if (stored is null)
        {
            return AuthTokenResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        if (stored.RevokedAtUtc is not null)
        {
            // A rotated-away token came back: assume it was stolen and kill every session for this user.
            LogRefreshTokenReuse(stored.UserId);
            await RevokeAllAsync(stored.UserId, now, cancellationToken);
            return AuthTokenResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        var user = await userManager.FindByIdAsync(stored.UserId);
        if (!stored.IsActive(now) || user is null || await userManager.IsLockedOutAsync(user))
        {
            return AuthTokenResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        return AuthTokenResult.Success(await IssueTokensAsync(user, stored, cancellationToken));
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(refreshToken, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        if (stored is not null && stored.IsActive(now))
        {
            stored.RevokedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<AuthTokens> IssueTokensAsync(ApplicationUser user, RefreshToken? previous, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(PortalClaims.Subject, user.Id),
            new(PortalClaims.Name, user.FullName),
            new(PortalClaims.Email, user.Email ?? string.Empty),
            new(PortalClaims.TokenId, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange((await userManager.GetRolesAsync(user)).Select(role => new Claim(PortalClaims.Role, role)));

        var accessToken = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256)
        });

        var rawRefreshToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(rawRefreshToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.RefreshTokenDays)
        };
        db.RefreshTokens.Add(refresh);

        if (previous is not null)
        {
            previous.RevokedAtUtc = now;
            previous.ReplacedByTokenHash = refresh.TokenHash;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokens("Bearer", accessToken, (int)(expires - now).TotalSeconds, rawRefreshToken, refresh.ExpiresAtUtc);
    }

    private Task<RefreshToken?> FindAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = Hash(rawToken);
        return db.RefreshTokens.SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
    }

    private async Task RevokeAllAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAtUtc, now), cancellationToken);
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected for user {UserId}; all refresh tokens revoked")]
    private partial void LogRefreshTokenReuse(string userId);
}

using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DisputePortal.Infrastructure.Authentication;
using DisputePortal.Infrastructure.Persistence;
using DisputePortal.Api.ErrorHandling;
using DisputePortal.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace DisputePortal.Api.Extensions;

/// <summary>Everything the web host itself needs: the API, JWT authentication, policies, rate limiting.</summary>
public static class PresentationServices
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddPortalAuthentication()
            .AddPortalAuthorization()
            .AddPortalApi()
            .AddPortalRateLimiting();

        // Persist data-protection keys (Identity uses them for password-reset and similar tokens) when a path
        // is configured, so restarts and multiple instances agree on them.
        var dataProtection = services.AddDataProtection().SetApplicationName("DisputePortal");
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database", tags: ["ready"]);
        return services;
    }

    private static IServiceCollection AddPortalAuthentication(this IServiceCollection services)
    {
        // Every client, the React app included, authenticates with JWT bearer tokens. There is no cookie
        // sign-in: the only cookie is the HttpOnly refresh-token cookie, which only /api/auth reads.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false; // keep "sub"/"role" as issued
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = jwt.Value.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = PortalClaims.Name,
                    RoleClaimType = PortalClaims.Role
                };
            });

        return services;
    }

    private static IServiceCollection AddPortalAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Customer, p => p.RequireAuthenticatedUser().RequireRole(Application.Common.Roles.Customer))
            .AddPolicy(Policies.Agent, p => p.RequireAuthenticatedUser().RequireRole(Application.Common.Roles.Agent));
        return services;
    }

    private static IServiceCollection AddPortalApi(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Transactions Dispute Portal API";
            document.Info.Description =
                "POST /api/auth/token for a JWT, send it as 'Authorization: Bearer <token>', and use POST /api/auth/refresh before it expires.";
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token from POST /api/auth/token"
            };
            document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
            return Task.CompletedTask;
        }));

        return services;
    }

    private static IServiceCollection AddPortalRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimits.DisputeWrites, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindUserId() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => Window(context, o => o.DisputeWritesPerMinute)));

            options.AddPolicy(RateLimits.Authentication, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => Window(context, o => o.AuthenticationPerMinute)));
        });

        static FixedWindowRateLimiterOptions Window(HttpContext context, Func<RateLimitingOptions, int> limit) => new()
        {
            PermitLimit = limit(context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value),
            Window = TimeSpan.FromMinutes(1)
        };
    }
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Sign-in / token requests per client IP per minute.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 10_000)]
    public int AuthenticationPerMinute { get; set; } = 20;

    /// <summary>Dispute submissions / withdrawals per user per minute.</summary>
    [System.ComponentModel.DataAnnotations.Range(1, 10_000)]
    public int DisputeWritesPerMinute { get; set; } = 10;
}

using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DisputePortal.Infrastructure;
using DisputePortal.Infrastructure.Authentication;
using DisputePortal.Infrastructure.Persistence;
using DisputePortal.Web.ErrorHandling;
using DisputePortal.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace DisputePortal.Web.Extensions;

/// <summary>Everything the web host itself needs: MVC, authentication schemes, policies, API plumbing.</summary>
public static class PresentationServices
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddPortalAuthentication()
            .AddPortalAuthorization()
            .AddPortalMvc()
            .AddPortalApi()
            .AddPortalRateLimiting();

        // Persist data-protection keys (they encrypt auth cookies) when a path is configured, so restarts
        // and multiple instances don't sign everyone out.
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
        // Website: Identity cookies. API: JWT bearer tokens. Each endpoint picks via its policy.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        services.AddAuthentication().AddJwtBearer();
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

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "DisputePortal.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;

            // Explicit, so they don't depend on the order Identity UI and the cookie scheme were registered in.
            options.LoginPath = "/Identity/Account/Login";
            options.LogoutPath = "/Identity/Account/Logout";
            options.AccessDeniedPath = "/Identity/Account/AccessDenied";
        });

        return services;
    }

    private static IServiceCollection AddPortalAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Customer, p => p.RequireRole(Application.Common.Roles.Customer))
            .AddPolicy(Policies.Agent, p => p.RequireRole(Application.Common.Roles.Agent))
            .AddPolicy(Policies.ApiCustomer, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(Application.Common.Roles.Customer))
            .AddPolicy(Policies.ApiAgent, p => p
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(Application.Common.Roles.Agent));
        return services;
    }

    private static IServiceCollection AddPortalMvc(this IServiceCollection services)
    {
        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddRazorPages(options =>
        {
            // Customers are onboarded by the bank, not self-registered, so switch off the Identity UI pages
            // that create or delete accounts. Removing their routes makes them a plain 404.
            string[] disabled = ["/Account/Register", "/Account/RegisterConfirmation", "/Account/ExternalLogin", "/Account/Manage/DeletePersonalData"];
            options.Conventions.AddAreaFolderRouteModelConvention("Identity", "/Account", model =>
            {
                if (disabled.Contains(model.ViewEnginePath, StringComparer.OrdinalIgnoreCase))
                {
                    model.Selectors.Clear();
                }
            });
        });

        return services;
    }

    private static IServiceCollection AddPortalApi(this IServiceCollection services)
    {
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

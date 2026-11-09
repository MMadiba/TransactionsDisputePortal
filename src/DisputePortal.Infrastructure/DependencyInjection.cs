using DisputePortal.Application.Abstractions;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using DisputePortal.Infrastructure.Authentication;
using DisputePortal.Infrastructure.Identity;
using DisputePortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.Infrastructure;

public static class DependencyInjection
{
    /// <param name="configureIdentity">
    /// Lets the host extend Identity (e.g. add the default UI) without Infrastructure knowing about the web layer.
    /// </param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Action<IdentityBuilder>? configureIdentity = null)
    {
        // Identity's SignInManager needs the authentication core services; the host adds the actual schemes.
        services.AddAuthentication();
        var identity = services.AddPortalIdentity();
        configureIdentity?.Invoke(identity);

        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        services.AddOptions<DemoDataOptions>().BindConfiguration(DemoDataOptions.SectionName);
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        // Connection string is resolved lazily so hosts and tests can override configuration after registration.
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.")));

        // Driven adapters for the application's ports.
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITransactionReadModel, TransactionReadModel>();
        services.AddScoped<IDisputeReadModel, DisputeReadModel>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IAuthTokenService, AuthTokenService>();

        return services;
    }

    /// <summary>
    /// Identity core (users, roles, EF stores, sign-in manager, lockout). The host decides how people
    /// authenticate (cookies, JWT) and whether to add the Identity UI on top.
    /// </summary>
    private static IdentityBuilder AddPortalIdentity(this IServiceCollection services) =>
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Stores.MaxLengthForKeys = 128;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
}

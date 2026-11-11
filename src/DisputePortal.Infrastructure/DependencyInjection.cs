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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DisputePortal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Identity's SignInManager needs the authentication core services; the host adds the actual schemes.
        services.AddAuthentication();
        services.AddPortalIdentity();

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

        // Domain events are saved to an outbox with each change and handled in the background.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxWorker>();

        return services;
    }

    /// <summary>
    /// Identity core: users, roles, EF stores, password hashing and lockout. There are no Identity pages;
    /// people sign in through the token endpoint, which checks passwords with the sign-in manager.
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

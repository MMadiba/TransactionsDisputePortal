using DisputePortal.Application;
using DisputePortal.Application.Abstractions;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Transactions;
using DisputePortal.Infrastructure;
using DisputePortal.Infrastructure.Identity;
using DisputePortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.IntegrationTests.Persistence;

/// <summary>
/// The real Application + Infrastructure wiring (handlers, repositories, read models, unit of work,
/// event dispatch) over a real SQLite file built from the migrations. No web host, no fakes.
/// </summary>
public sealed class PersistenceFixture : IAsyncDisposable
{
    public static readonly DateTime Now = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);
    public const string CustomerId = "customer-1";
    public const string OtherCustomerId = "customer-2";
    public const string AgentId = "agent-1";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"dp-persistence-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;

    public PersistenceFixture()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={_path};Pooling=False",
            ["Portal:TimeZone"] = "Africa/Johannesburg"
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(Now)));
        services.AddSingleton<RecordingEventHandler>();
        services.AddScoped<IDomainEventHandler<Domain.Disputes.DisputeOpened>>(sp => sp.GetRequiredService<RecordingEventHandler>());
        services.AddApplication().AddInfrastructure();
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
        db.Users.AddRange(
            new ApplicationUser { Id = CustomerId, UserName = "c@example.com", Email = "c@example.com", FullName = "Casey Customer" },
            new ApplicationUser { Id = OtherCustomerId, UserName = "o@example.com", Email = "o@example.com", FullName = "Olu Other" },
            new ApplicationUser { Id = AgentId, UserName = "a@example.com", Email = "a@example.com", FullName = "Avery Agent" });
        db.SaveChanges();
    }

    public RecordingEventHandler Events => _services.GetRequiredService<RecordingEventHandler>();

    /// <summary>Does what the background outbox worker does, on demand.</summary>
    public Task<int> ProcessOutboxAsync() => _services.GetRequiredService<OutboxProcessor>().ProcessPendingAsync();

    /// <summary>Runs <paramref name="action"/> in its own DI scope, like a single web request.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<TResult> SendAsync<TCommand, TResult>(TCommand command) =>
        InScopeAsync(sp => sp.GetRequiredService<ICommandHandler<TCommand, TResult>>().HandleAsync(command));

    public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query) =>
        InScopeAsync(sp => sp.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query));

    public Task<Transaction> AddTransactionAsync(
        string customerId = CustomerId, int daysAgo = 2, long amountMinor = -25_000,
        TransactionStatus status = TransactionStatus.Posted, string merchant = "Corner Store") =>
        InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var tx = new Transaction(customerId, $"TXN{Guid.NewGuid():N}"[..20], Now.AddDays(-daysAgo), merchant,
                "Test purchase", new Money(amountMinor, "ZAR"), status, "4242");
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();
            return tx;
        });

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        try { File.Delete(_path); } catch (IOException) { /* best effort */ }
    }
}

public sealed class RecordingEventHandler : IDomainEventHandler<Domain.Disputes.DisputeOpened>
{
    public List<Domain.Disputes.DisputeOpened> Received { get; } = [];

    /// <summary>When set, the handler throws this instead of recording the event.</summary>
    public Exception? FailWith { get; set; }

    public Task HandleAsync(Domain.Disputes.DisputeOpened domainEvent, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Received.Add(domainEvent);
        return Task.CompletedTask;
    }
}

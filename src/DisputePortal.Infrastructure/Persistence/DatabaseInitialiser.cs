using DisputePortal.Application.Common;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using DisputePortal.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputePortal.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply EF Core migrations at startup. Turn off if migrations run as a separate deploy step.</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}

public sealed class DemoDataOptions
{
    public const string SectionName = "DemoData";

    /// <summary>Seed demo customers, an agent and sample transactions into an empty database.</summary>
    public bool Enabled { get; set; }

    /// <summary>Password given to every seeded demo account.</summary>
    public string Password { get; set; } = string.Empty;
}

public static class DatabaseInitialiser
{
    /// <summary>Applies migrations (if enabled), makes sure the roles exist and seeds demo data (if enabled).</summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider rootServices)
    {
        await using var scope = rootServices.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitialiser));
        var db = services.GetRequiredService<ApplicationDbContext>();

        EnsureSqliteDirectoryExists(db);

        if (services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync();
        }

        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole(role));
            }
        }

        var demo = services.GetRequiredService<IOptions<DemoDataOptions>>().Value;
        if (demo.Enabled)
        {
            await DemoDataSeeder.SeedAsync(db, services.GetRequiredService<UserManager<ApplicationUser>>(),
                services.GetRequiredService<IPortalClock>(), services.GetRequiredService<DisputeEligibilityPolicy>(), demo, logger);
        }
    }

    private static void EnsureSqliteDirectoryExists(ApplicationDbContext db)
    {
        var builder = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if (builder.DataSource is ":memory:" or "")
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

/// <summary>
/// Seeds a realistic, deterministic data set so the portal is useful the moment it starts: two customers
/// with six months of card activity (a duplicate charge, pending items, credits, transactions outside the
/// dispute window) and disputes in every state. Everything goes through the domain model, so the seed data
/// obeys the same rules as real data.
/// </summary>
internal static class DemoDataSeeder
{
    // Demo accounts on the bank's own domain. Nothing is ever emailed to them: the portal uses a no-op email sender.
    public const string AgentEmail = "naledi.khumalo@mmbank.co.za";
    public const string AmandaEmail = "amanda.mitchell@mmbank.co.za";
    public const string SiphoEmail = "sipho.dlamini@mmbank.co.za";
    public const string AyandaEmail = "ayanda.nkosi@mmbank.co.za";
    private const string DuplicateChargeMerchant = "Kauai";
    private const string Currency = "ZAR";

    private static readonly (string Merchant, string Description, int MinRand, int MaxRand)[] Merchants =
    [
        ("Woolworths Food", "Groceries", 150, 1400),
        ("Checkers Sixty60", "Grocery delivery", 200, 1100),
        ("Uber", "Ride", 45, 260),
        ("Netflix", "Monthly subscription", 199, 199),
        ("Takealot", "Online order", 120, 3500),
        ("Engen Garage", "Fuel", 400, 1200),
        ("Vodacom", "Airtime & data", 99, 599),
        ("Dis-Chem", "Pharmacy", 80, 900),
        ("Mugg & Bean", "Restaurant", 90, 420),
        ("Spotify", "Monthly subscription", 70, 70),
        ("Mr Price", "Clothing", 150, 1600),
        ("Bolt Food", "Food delivery", 110, 380)
    ];

    public static async Task SeedAsync(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        IPortalClock clock,
        DisputeEligibilityPolicy policy,
        DemoDataOptions options,
        ILogger logger)
    {
        if (await db.Users.AnyAsync())
        {
            return; // Never touch a database that already has people in it.
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException("DemoData:Password must be set when DemoData:Enabled is true.");
        }

        logger.LogWarning("Seeding demo data. Disable DemoData:Enabled for real deployments.");

        var agentUser = await CreateUserAsync(users, AgentEmail, "Naledi Khumalo", Roles.Agent, options.Password);
        var amandaUser = await CreateUserAsync(users, AmandaEmail, "Amanda Mitchell", Roles.Customer, options.Password);
        var siphoUser = await CreateUserAsync(users, SiphoEmail, "Sipho Dlamini", Roles.Customer, options.Password);
        var ayandaUser = await CreateUserAsync(users, AyandaEmail, "Ayanda Nkosi", Roles.Customer, options.Password);

        var now = clock.UtcNow;
        var amandaTransactions = GenerateTransactions(amandaUser.Id, "4821", seed: 7, now);
        var siphoTransactions = GenerateTransactions(siphoUser.Id, "9034", seed: 11, now);
        var ayandaTransactions = GenerateTransactions(ayandaUser.Id, "5517", seed: 13, now);
        db.Transactions.AddRange(amandaTransactions);
        db.Transactions.AddRange(siphoTransactions);
        db.Transactions.AddRange(ayandaTransactions);

        var amanda = Actor.Customer(amandaUser.Id, amandaUser.FullName);
        var agent = Actor.Agent(agentUser.Id, agentUser.FullName);

        // Leave the duplicate charge alone so whoever tries the portal can dispute it themselves.
        var eligible = amandaTransactions
            .Where(t => t.Amount.IsDebit && t.Status == TransactionStatus.Posted && t.PostedAtUtc > now.AddDays(-100))
            .Where(t => t.Merchant != DuplicateChargeMerchant)
            .OrderBy(t => t.PostedAtUtc)
            .ToList();

        Dispute OpenAt(Transaction t, DisputeReason reason, string details, DateTime at) =>
            Dispute.Open(t, null, policy, reason, details, amanda, at);

        var upheld = OpenAt(eligible[0], DisputeReason.GoodsOrServicesNotReceived,
            "I paid for an order that never arrived. The merchant stopped replying to my emails.", eligible[0].PostedAtUtc.AddDays(3));
        upheld.StartReview(agent, upheld.CreatedAtUtc.AddDays(1));
        upheld.Resolve(DisputeOutcome.Upheld, "We confirmed the merchant did not deliver. The full amount has been refunded to your card.",
            agent, upheld.CreatedAtUtc.AddDays(6));

        var declined = OpenAt(eligible[3], DisputeReason.IncorrectAmount,
            "I was charged more than the price on the shelf when I checked out.", eligible[3].PostedAtUtc.AddDays(2));
        declined.StartReview(agent, declined.CreatedAtUtc.AddDays(2));
        declined.Resolve(DisputeOutcome.Declined, "The merchant supplied an itemised slip that matches the amount charged, so we can't reverse this one.",
            agent, declined.CreatedAtUtc.AddDays(9));

        var withdrawn = OpenAt(eligible[6], DisputeReason.UnrecognisedTransaction,
            "I didn't recognise this at first. Turns out my partner used my card.", eligible[6].PostedAtUtc.AddDays(1));
        withdrawn.Withdraw(amanda, withdrawn.CreatedAtUtc.AddHours(5));

        var inReview = OpenAt(eligible[^4], DisputeReason.CancelledSubscription,
            "I cancelled this subscription last month but was still billed again this month.", eligible[^4].PostedAtUtc.AddDays(1));
        inReview.StartReview(agent, inReview.CreatedAtUtc.AddDays(1));

        // Ayanda has one brand-new dispute, so the agent's queue always has something waiting to be picked up.
        var ayandaDebit = ayandaTransactions
            .Where(t => t.Amount.IsDebit && t.Status == TransactionStatus.Posted && t.Merchant != DuplicateChargeMerchant)
            .OrderByDescending(t => t.PostedAtUtc)
            .Skip(2)
            .First();
        var submitted = Dispute.Open(ayandaDebit, null, policy, DisputeReason.UnrecognisedTransaction,
            "I don't recognise this merchant and I still have my card with me.",
            Actor.Customer(ayandaUser.Id, ayandaUser.FullName), ayandaDebit.PostedAtUtc.AddDays(1));

        Dispute[] disputes = [upheld, declined, withdrawn, inReview, submitted];
        foreach (var dispute in disputes)
        {
            dispute.ClearDomainEvents(); // historical seed data, not things happening now
        }

        db.Disputes.AddRange(disputes);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded {TransactionCount} transactions and {DisputeCount} disputes",
            amandaTransactions.Count + siphoTransactions.Count + ayandaTransactions.Count, disputes.Length);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> users, string email, string fullName, string role, string password)
    {
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = fullName };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not seed {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await users.AddToRoleAsync(user, role);
        return user;
    }

#pragma warning disable CA5394 // Not security sensitive: deterministic demo data only.
    private static List<Transaction> GenerateTransactions(string customerId, string cardLast4, int seed, DateTime nowUtc)
    {
        var random = new Random(seed);
        var list = new List<Transaction>();
        var sequence = 0;

        Transaction Make(DateTime at, string merchant, string description, long amountMinor, TransactionStatus status = TransactionStatus.Posted) =>
            new(customerId, $"TXN{at:yyyyMMdd}{seed:D2}{++sequence:D4}", at, merchant, description,
                new Money(amountMinor, Currency), status, cardLast4);

        for (var daysAgo = 180; daysAgo >= 2; daysAgo -= random.Next(1, 4))
        {
            var (merchant, description, min, max) = Merchants[random.Next(Merchants.Length)];
            var at = nowUtc.Date.AddDays(-daysAgo).AddHours(random.Next(7, 21)).AddMinutes(random.Next(60));
            list.Add(Make(DateTime.SpecifyKind(at, DateTimeKind.Utc), merchant, description, -(long)random.Next(min * 100, (max * 100) + 1)));
        }
#pragma warning restore CA5394

        // Monthly salary credits and a merchant refund: credits can't be disputed.
        for (var month = 0; month < 6; month++)
        {
            var payday = new DateTime(nowUtc.Year, nowUtc.Month, 25, 6, 0, 0, DateTimeKind.Utc).AddMonths(-month);
            if (payday < nowUtc)
            {
                list.Add(Make(payday, "Acme Holdings", "Salary", 3_250_000));
            }
        }
        list.Add(Make(nowUtc.AddDays(-20), "Takealot", "Refund", 45_900));

        // A classic duplicate charge, recent enough to still be disputable.
        var duplicateAt = DateTime.SpecifyKind(nowUtc.Date.AddDays(-5).AddHours(13).AddMinutes(12), DateTimeKind.Utc);
        list.Add(Make(duplicateAt, DuplicateChargeMerchant, "Restaurant", -12_950));
        list.Add(Make(duplicateAt.AddMinutes(1), DuplicateChargeMerchant, "Restaurant", -12_950));

        // Still-pending card authorisations.
        list.Add(Make(nowUtc.AddHours(-20), "Uber", "Ride", -14_300, TransactionStatus.Pending));
        list.Add(Make(nowUtc.AddHours(-3), "Woolworths Food", "Groceries", -38_475, TransactionStatus.Pending));

        return list;
    }
}

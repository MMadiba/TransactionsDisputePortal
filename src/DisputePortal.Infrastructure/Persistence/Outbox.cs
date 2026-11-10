using System.Collections.Frozen;
using System.Text.Json;
using DisputePortal.Application.Abstractions;
using DisputePortal.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DisputePortal.Infrastructure.Persistence;

/// <summary>
/// A domain event waiting to be handled. It is written in the same database transaction as the change that
/// raised it, so the event can't be lost if the process dies after saving, and a failing handler can't turn a
/// saved change into an error for the user.
/// </summary>
internal sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Type { get; init; }
    public required string Content { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Type).HasMaxLength(200).IsRequired();
        b.Property(m => m.Content).IsRequired();
        b.Property(m => m.LastError).HasMaxLength(2000);
        b.HasIndex(m => new { m.ProcessedAtUtc, m.CreatedAtUtc });
    }
}

/// <summary>Turns domain events into outbox rows and back. Only event types from the domain assembly are accepted.</summary>
internal static class OutboxSerializer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly FrozenDictionary<string, Type> EventTypes = typeof(IDomainEvent).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
        .ToFrozenDictionary(t => t.Name);

    public static OutboxMessage ToMessage(IDomainEvent domainEvent, DateTime nowUtc) => new()
    {
        Type = domainEvent.GetType().Name,
        Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Json),
        CreatedAtUtc = nowUtc
    };

    public static IDomainEvent ToEvent(OutboxMessage message) =>
        EventTypes.TryGetValue(message.Type, out var type)
            ? (IDomainEvent)(JsonSerializer.Deserialize(message.Content, type, Json)
                ?? throw new InvalidOperationException($"Outbox message {message.Id} has no content."))
            : throw new InvalidOperationException($"Unknown domain event type '{message.Type}'.");
}

/// <summary>Wakes the outbox worker as soon as something is saved, instead of waiting for the next poll.</summary>
internal sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        // At most one pending wake-up: the worker drains everything waiting when it runs.
        if (_signal.CurrentCount == 0)
        {
            try { _signal.Release(); } catch (SemaphoreFullException) { /* already signalled */ }
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>
/// Hands pending outbox messages to their domain event handlers. Delivery is at-least-once: if the process
/// stops between handling a message and marking it done, it is handled again, so handlers must be idempotent.
/// A message that keeps failing is retried up to <see cref="MaxAttempts"/> times, then left for someone to look at.
/// </summary>
public sealed partial class OutboxProcessor(IServiceScopeFactory scopes, TimeProvider time, ILogger<OutboxProcessor> logger)
{
    public const int MaxAttempts = 5;
    private const int BatchSize = 50;

    /// <summary>Processes one batch of pending messages and returns how many were handled successfully.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var handled = 0;
        foreach (var message in pending)
        {
            try
            {
                await dispatcher.DispatchAsync([OutboxSerializer.ToEvent(message)], cancellationToken);
                message.ProcessedAtUtc = time.GetUtcNow().UtcDateTime;
                message.LastError = null;
                handled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (message.Attempts >= MaxAttempts)
                {
                    LogGaveUp(ex, message.Id, message.Type, message.Attempts);
                }
                else
                {
                    LogFailed(ex, message.Id, message.Type, message.Attempts);
                }
            }

            // Record each outcome straight away, so one bad message can't make the others run twice.
            await db.SaveChangesAsync(cancellationToken);
        }

        return handled;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({EventType}) failed on attempt {Attempt}; it will be retried")]
    private partial void LogFailed(Exception exception, Guid messageId, string eventType, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({EventType}) failed {Attempts} times and won't be retried")]
    private partial void LogGaveUp(Exception exception, Guid messageId, string eventType, int attempts);
}

/// <summary>Runs the <see cref="OutboxProcessor"/> whenever something is saved, and every few seconds to pick up retries.</summary>
internal sealed partial class OutboxWorker(OutboxProcessor processor, OutboxSignal signal, ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ProcessPendingAsync(stoppingToken);
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database being briefly unavailable mustn't kill the worker; try again on the next tick.
                LogLoopFailed(ex);
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox worker failed to process messages")]
    private partial void LogLoopFailed(Exception exception);
}

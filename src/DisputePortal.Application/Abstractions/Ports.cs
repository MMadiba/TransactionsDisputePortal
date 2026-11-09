using DisputePortal.Application.Contracts;

namespace DisputePortal.Application.Abstractions;

/// <summary>Commits everything the repositories have tracked as one atomic unit, then publishes domain events.</summary>
public interface IUnitOfWork
{
    /// <exception cref="Common.ConcurrencyConflictException">Someone else changed the same aggregate first.</exception>
    /// <exception cref="Common.UniqueConstraintConflictException">A uniqueness rule in the store was violated.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Looks up people by id. Identity is an infrastructure concern; the application only needs names.</summary>
public interface IUserDirectory
{
    Task<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>Optimised read side for transactions (CQRS-lite: queries skip the aggregates and project straight to DTOs).</summary>
public interface ITransactionReadModel
{
    Task<PagedResult<TransactionSummary>> SearchAsync(TransactionSearchCriteria criteria, CancellationToken cancellationToken = default);
}

/// <summary>Optimised read side for disputes.</summary>
public interface IDisputeReadModel
{
    Task<PagedResult<DisputeSummary>> SearchAsync(DisputeSearchCriteria criteria, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisputeSummary>> ListForTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Full detail with the raw audit trail. Pass <paramref name="customerId"/> to restrict to that customer's disputes.</summary>
    Task<DisputeDetail?> GetAsync(Guid disputeId, string? customerId, CancellationToken cancellationToken = default);
}

public sealed record TransactionSearchCriteria(
    string CustomerId, string? Search, DateTime? FromUtc, DateTime? ToUtcExclusive, int Page, int PageSize);

public enum DisputeSortOrder
{
    NewestFirst,
    OldestFirst,
    RecentlyUpdated
}

public sealed record DisputeSearchCriteria(string? CustomerId, DisputeFilter Filter, DisputeSortOrder Sort, int Page, int PageSize);

/// <summary>Issues and rotates API credentials (JWT access tokens + refresh tokens).</summary>
public interface IAuthTokenService
{
    Task<AuthTokenResult> IssueAsync(string email, string password, CancellationToken cancellationToken = default);

    Task<AuthTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes a refresh token (sign out). Unknown tokens are ignored so this can't be used to probe.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}

using System.ComponentModel.DataAnnotations;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.Application.Contracts;

// ---------- Responses (DTOs: the only shapes that leave the application layer) ----------

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public sealed record DisputeLink(Guid Id, string Reference, DisputeStatus Status);

public sealed record TransactionSummary(
    Guid Id,
    string Reference,
    DateTime PostedAtUtc,
    string Merchant,
    string Description,
    decimal Amount,
    string Currency,
    TransactionStatus Status,
    DisputeLink? LatestDispute);

public sealed record TransactionDetail(
    Guid Id,
    string Reference,
    DateTime PostedAtUtc,
    string Merchant,
    string Description,
    decimal Amount,
    string Currency,
    TransactionStatus Status,
    string CardLast4,
    bool CanDispute,
    string? CannotDisputeReason,
    IReadOnlyList<DisputeSummary> Disputes);

public sealed record DisputeSummary(
    Guid Id,
    string Reference,
    Guid TransactionId,
    string TransactionReference,
    string Merchant,
    decimal Amount,
    string Currency,
    DisputeReason Reason,
    DisputeStatus Status,
    string CustomerName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ClosedAtUtc);

public sealed record DisputeHistoryItem(DisputeStatus Status, ActorRole ActorRole, string Actor, string? Note, DateTime OccurredAtUtc);

public sealed record DisputeDetail(
    Guid Id,
    string Reference,
    DisputeStatus Status,
    DisputeReason Reason,
    string Details,
    string? ResolutionNote,
    string CustomerName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ClosedAtUtc,
    TransactionSummary Transaction,
    IReadOnlyList<DisputeHistoryItem> Timeline,
    bool IsOpen);

public sealed record AuthTokens(
    string TokenType, string AccessToken, int ExpiresIn, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

public enum AuthFailure
{
    InvalidCredentials,
    LockedOut,
    InvalidRefreshToken
}

public sealed record AuthTokenResult(AuthTokens? Tokens, AuthFailure? Failure)
{
    public bool Succeeded => Tokens is not null;
    public static AuthTokenResult Success(AuthTokens tokens) => new(tokens, null);
    public static AuthTokenResult Fail(AuthFailure failure) => new(null, failure);
}

public enum DisputeFilter
{
    All,
    Open,
    Closed
}

// ---------- Requests ----------
// Mutable classes (not records) so MVC forms can bind to them. The same DataAnnotations drive the HTML
// form, the API's automatic 400 response and the handler's own check.

public sealed class OpenDisputeRequest
{
    [Required(ErrorMessage = "Tell us what went wrong.")]
    [Display(Name = "What went wrong?")]
    public DisputeReason? Reason { get; set; }

    [Required(ErrorMessage = "Please describe the problem.")]
    [StringLength(DisputeLimits.DetailsMaxLength, MinimumLength = DisputeLimits.DetailsMinLength,
        ErrorMessage = "Please give us between {2} and {1} characters so we can investigate.")]
    [Display(Name = "Tell us more")]
    public string Details { get; set; } = string.Empty;
}

public sealed class ResolveDisputeRequest
{
    [Required(ErrorMessage = "Choose an outcome.")]
    public DisputeOutcome? Outcome { get; set; }

    [Required(ErrorMessage = "A note for the customer is required.")]
    [StringLength(DisputeLimits.ResolutionNoteMaxLength, MinimumLength = DisputeLimits.ResolutionNoteMinLength,
        ErrorMessage = "The note must be between {2} and {1} characters.")]
    [Display(Name = "Note to the customer")]
    public string Note { get; set; } = string.Empty;
}

public sealed record AccessTokenRequest([Required, EmailAddress] string Email, [Required] string Password);

public sealed record RefreshTokenRequest([Required] string RefreshToken);

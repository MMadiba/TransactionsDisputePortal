using System.Globalization;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Domain.Disputes;

namespace DisputePortal.Web.Models;

/// <summary>Who a shared dispute partial is rendering for. Customers never see agents' names.</summary>
public enum Audience
{
    Customer,
    Agent
}

/// <summary>Date formatting for views. Formatting is presentation, so it lives here, not in the application layer.</summary>
public static class PortalClockFormatting
{
    public static string FormatDate(this IPortalClock clock, DateTime utc) =>
        clock.ToLocal(utc).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string FormatDateTime(this IPortalClock clock, DateTime utc) =>
        clock.ToLocal(utc).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}

public sealed record TransactionsIndexViewModel(
    PagedResult<TransactionSummary> Result, string? Search, DateOnly? From, DateOnly? To, int WindowDays)
{
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || From.HasValue || To.HasValue;
}

public sealed record TransactionDetailsViewModel(TransactionDetail Transaction, OpenDisputeRequest Form);

public sealed record DisputesIndexViewModel(PagedResult<DisputeSummary> Result, DisputeFilter Filter);

public sealed record DisputeDetailsViewModel(DisputeDetail Dispute, ResolveDisputeRequest? ResolveForm = null);

/// <summary>Model for the shared pager partial. RouteValues carries the current filters across pages.</summary>
public sealed record PagerViewModel(int Page, int TotalPages, int TotalCount, string Action, IDictionary<string, string?> RouteValues)
{
    public IDictionary<string, string?> For(int page) => new Dictionary<string, string?>(RouteValues) { ["page"] = page.ToString(CultureInfo.InvariantCulture) };

    public static PagerViewModel Create<T>(PagedResult<T> result, IDictionary<string, string?> routeValues, string action = "Index") =>
        new(result.Page, result.TotalPages, result.TotalCount, action, routeValues);
}

/// <summary>Customer-friendly wording and styling for enum values, kept out of the views.</summary>
public static class Display
{
    public static string Reason(DisputeReason reason) => reason switch
    {
        DisputeReason.UnrecognisedTransaction => "I don't recognise this transaction",
        DisputeReason.DuplicateCharge => "I was charged more than once",
        DisputeReason.IncorrectAmount => "The amount is wrong",
        DisputeReason.GoodsOrServicesNotReceived => "I didn't receive what I paid for",
        DisputeReason.CancelledSubscription => "I cancelled, but was still charged",
        DisputeReason.RefundNotReceived => "I was promised a refund that never came",
        _ => "Something else"
    };

    public static string Status(DisputeStatus status) => status switch
    {
        DisputeStatus.Submitted => "Submitted",
        DisputeStatus.UnderReview => "Under review",
        DisputeStatus.Upheld => "Resolved in your favour",
        DisputeStatus.Declined => "Declined",
        DisputeStatus.Withdrawn => "Withdrawn",
        _ => status.ToString()
    };

    public static string AgentStatus(DisputeStatus status) => status == DisputeStatus.Upheld ? "Upheld" : Status(status);

    public static string StatusBadge(DisputeStatus status) => status switch
    {
        DisputeStatus.Submitted => "text-bg-primary",
        DisputeStatus.UnderReview => "text-bg-warning",
        DisputeStatus.Upheld => "text-bg-success",
        DisputeStatus.Declined => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    public static string Filter(DisputeFilter filter) => filter switch
    {
        DisputeFilter.Open => "Open",
        DisputeFilter.Closed => "Closed",
        _ => "All"
    };
}

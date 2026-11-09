using DisputePortal.Domain.Transactions;

namespace DisputePortal.Domain.Disputes;

public sealed record EligibilityResult(bool CanDispute, string? Code = null, string? Message = null, bool IsConflict = false)
{
    public static readonly EligibilityResult Eligible = new(true);
}

/// <summary>
/// Domain service holding the rules for whether a transaction can be disputed. Used by the
/// <see cref="Dispute"/> factory to enforce them, and by queries to explain them, so the UI and the
/// rules can never disagree.
/// </summary>
public sealed class DisputeEligibilityPolicy
{
    public DisputeEligibilityPolicy(int windowDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowDays, 1);
        WindowDays = windowDays;
    }

    public int WindowDays { get; }

    public EligibilityResult Evaluate(Transaction transaction, Dispute? liveDispute, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (liveDispute is not null)
        {
            return new EligibilityResult(false, "dispute.already_exists",
                $"This transaction already has a dispute ({liveDispute.Reference}).", IsConflict: true);
        }

        if (transaction.Status == TransactionStatus.Pending)
        {
            return new EligibilityResult(false, "transaction.pending",
                "Pending transactions can't be disputed yet. Once it has posted you'll be able to raise a dispute.");
        }

        if (!transaction.Amount.IsDebit)
        {
            return new EligibilityResult(false, "transaction.not_a_debit",
                "Only money that left your account can be disputed. This transaction is a credit.");
        }

        if (transaction.PostedAtUtc < nowUtc.AddDays(-WindowDays))
        {
            return new EligibilityResult(false, "transaction.outside_window",
                $"Transactions can only be disputed within {WindowDays} days of posting. Please call us and we'll help.");
        }

        return EligibilityResult.Eligible;
    }
}

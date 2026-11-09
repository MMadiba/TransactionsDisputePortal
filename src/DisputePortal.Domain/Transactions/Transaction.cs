using DisputePortal.Domain.Common;

namespace DisputePortal.Domain.Transactions;

public enum TransactionStatus
{
    Pending,
    Posted
}

/// <summary>
/// A card transaction on a customer's account. Transactions arrive from the core banking system and are
/// never changed by this portal, so the aggregate is read-only once created.
/// </summary>
public sealed class Transaction : AggregateRoot
{
    private Transaction() { } // EF Core

    public Transaction(
        string customerId,
        string reference,
        DateTime postedAtUtc,
        string merchant,
        string description,
        Money amount,
        TransactionStatus status,
        string cardLast4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(merchant);
        ArgumentNullException.ThrowIfNull(amount);
        if (postedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Times must be UTC.", nameof(postedAtUtc));
        }

        Id = Guid.CreateVersion7();
        CustomerId = customerId;
        Reference = reference;
        PostedAtUtc = postedAtUtc;
        Merchant = merchant;
        Description = description;
        Amount = amount;
        Status = status;
        CardLast4 = cardLast4;
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public string Reference { get; private set; } = default!;
    public DateTime PostedAtUtc { get; private set; }
    public string Merchant { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public Money Amount { get; private set; } = default!;
    public TransactionStatus Status { get; private set; }
    public string CardLast4 { get; private set; } = default!;

    public bool IsOwnedBy(string customerId) => CustomerId == customerId;
}

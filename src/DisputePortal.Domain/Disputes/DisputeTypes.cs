using System.Security.Cryptography;

namespace DisputePortal.Domain.Disputes;

public enum DisputeReason
{
    UnrecognisedTransaction,
    DuplicateCharge,
    IncorrectAmount,
    GoodsOrServicesNotReceived,
    CancelledSubscription,
    RefundNotReceived,
    Other
}

public enum DisputeStatus
{
    Submitted,
    UnderReview,
    Upheld,
    Declined,
    Withdrawn
}

public enum DisputeOutcome
{
    Upheld,
    Declined
}

public enum ActorRole
{
    Customer,
    Agent,
    System
}

/// <summary>Value object: who did something to a dispute.</summary>
public sealed record Actor
{
    private Actor(string userId, string name, ActorRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UserId = userId;
        Name = name;
        Role = role;
    }

    public string UserId { get; }
    public string Name { get; }
    public ActorRole Role { get; }

    public static Actor Customer(string userId, string name) => new(userId, name, ActorRole.Customer);

    public static Actor Agent(string userId, string name) => new(userId, name, ActorRole.Agent);
}

/// <summary>
/// Short, human-friendly dispute references such as <c>DSP-7K3Q9ABX</c>. The alphabet leaves out
/// 0/O and 1/I so a customer can read one over the phone without confusion.
/// </summary>
public static class DisputeReference
{
    public const int MaxLength = 12;
    private const string Prefix = "DSP-";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New() => Prefix + RandomNumberGenerator.GetString(Alphabet, MaxLength - Prefix.Length);
}

public static class DisputeLimits
{
    public const int DetailsMinLength = 20;
    public const int DetailsMaxLength = 2000;
    public const int ResolutionNoteMinLength = 10;
    public const int ResolutionNoteMaxLength = 1000;
}

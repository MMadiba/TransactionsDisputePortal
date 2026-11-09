using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.UnitTests.Domain;

public sealed class DisputeTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DisputeEligibilityPolicy Policy = new(windowDays: 120);
    private static readonly Actor Customer = Actor.Customer("customer-1", "Casey");
    private static readonly Actor Agent = Actor.Agent("agent-1", "Avery");
    private const string Details = "I have never shopped at this merchant.";

    internal static Transaction Debit(int daysAgo = 3, TransactionStatus status = TransactionStatus.Posted, long amount = -10_000, string customerId = "customer-1") =>
        new(customerId, "TXN1", Now.AddDays(-daysAgo), "Coffee Co", "Coffee", new Money(amount, "ZAR"), status, "1234");

    private static Dispute Open(Transaction? transaction = null) =>
        Dispute.Open(transaction ?? Debit(), null, Policy, DisputeReason.DuplicateCharge, Details, Customer, Now);

    [Fact]
    public void Opening_records_history_raises_an_event_and_gets_a_readable_reference()
    {
        var dispute = Open();

        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        Assert.Matches("^DSP-[A-HJ-NP-Z2-9]{8}$", dispute.Reference);
        var entry = Assert.Single(dispute.History);
        Assert.Equal((DisputeStatus.Submitted, ActorRole.Customer, "Casey"), (entry.Status, entry.ActorRole, entry.ActorName));
        var opened = Assert.IsType<DisputeOpened>(Assert.Single(dispute.DomainEvents));
        Assert.Equal(dispute.Id, opened.DisputeId);
    }

    [Fact]
    public void Only_the_account_holder_can_open_a_dispute()
    {
        var someoneElses = Debit(customerId: "customer-2");

        var ex = Assert.Throws<BusinessRuleViolationException>(() => Open(someoneElses));

        Assert.Equal("dispute.not_owner", ex.Code);
    }

    [Theory]
    [InlineData(TransactionStatus.Pending, -100, 1, "transaction.pending")]
    [InlineData(TransactionStatus.Posted, 100, 1, "transaction.not_a_debit")]
    [InlineData(TransactionStatus.Posted, -100, 121, "transaction.outside_window")]
    public void The_aggregate_enforces_the_eligibility_rules(TransactionStatus status, long amount, int daysAgo, string expectedCode)
    {
        var ex = Assert.Throws<BusinessRuleViolationException>(() => Open(Debit(daysAgo, status, amount)));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public void A_live_dispute_blocks_another_as_a_conflict()
    {
        var transaction = Debit();
        var existing = Open(transaction);

        var ex = Assert.Throws<DomainConflictException>(() =>
            Dispute.Open(transaction, existing, Policy, DisputeReason.Other, Details, Customer, Now));

        Assert.Equal("dispute.already_exists", ex.Code);
        Assert.Contains(existing.Reference, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Details_must_be_meaningful()
    {
        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            Dispute.Open(Debit(), null, Policy, DisputeReason.Other, "   too short   ", Customer, Now));

        Assert.Equal("dispute.details_length", ex.Code);
    }

    [Fact]
    public void Full_review_path_builds_an_ordered_audit_trail_and_events()
    {
        var dispute = Open();
        var versionAfterOpen = dispute.Version;
        dispute.ClearDomainEvents();

        dispute.StartReview(Agent, Now.AddHours(1));
        dispute.Resolve(DisputeOutcome.Upheld, "Refunded in full.", Agent, Now.AddHours(2));

        Assert.Equal(DisputeStatus.Upheld, dispute.Status);
        Assert.Equal(Now.AddHours(2), dispute.ClosedAtUtc);
        Assert.False(dispute.IsOpen);
        Assert.NotEqual(versionAfterOpen, dispute.Version);
        Assert.Equal([DisputeStatus.Submitted, DisputeStatus.UnderReview, DisputeStatus.Upheld], dispute.History.Select(h => h.Status));
        Assert.Collection(dispute.DomainEvents,
            e => Assert.IsType<DisputeReviewStarted>(e),
            e => Assert.Equal(DisputeOutcome.Upheld, Assert.IsType<DisputeResolved>(e).Outcome));
    }

    [Fact]
    public void A_closed_dispute_cannot_be_withdrawn_or_resolved_again()
    {
        var dispute = Open();
        dispute.Resolve(DisputeOutcome.Declined, "The charge is valid.", Agent, Now.AddDays(1));

        Assert.Equal("dispute.not_open", Assert.Throws<DomainConflictException>(() => dispute.Withdraw(Customer, Now.AddDays(2))).Code);
        Assert.Throws<DomainConflictException>(() => dispute.Resolve(DisputeOutcome.Upheld, "Changed our mind", Agent, Now.AddDays(2)));
    }

    [Fact]
    public void Review_can_only_start_from_submitted_and_only_by_an_agent()
    {
        var dispute = Open();

        Assert.Equal("dispute.agent_only", Assert.Throws<BusinessRuleViolationException>(() => dispute.StartReview(Customer, Now)).Code);
        dispute.StartReview(Agent, Now);
        Assert.Equal("dispute.invalid_state", Assert.Throws<DomainConflictException>(() => dispute.StartReview(Agent, Now)).Code);
    }

    [Fact]
    public void Only_the_customer_who_raised_it_can_withdraw()
    {
        var dispute = Open();

        var ex = Assert.Throws<BusinessRuleViolationException>(() => dispute.Withdraw(Actor.Customer("customer-2", "Olu"), Now));

        Assert.Equal("dispute.not_owner", ex.Code);
    }

    [Fact]
    public void Withdrawn_disputes_stop_blocking_new_ones()
    {
        var dispute = Open();
        Assert.True(dispute.IsLive);

        dispute.Withdraw(Customer, Now);

        Assert.False(dispute.IsLive);
        Assert.IsType<DisputeWithdrawn>(dispute.DomainEvents.Last());
    }

    [Fact]
    public void The_last_day_of_the_window_is_still_eligible()
    {
        Assert.True(Policy.Evaluate(Debit(daysAgo: 120), null, Now).CanDispute);
    }
}

public sealed class MoneyTests
{
    [Theory]
    [InlineData(-125_000, "-ZAR 1,250.00")]
    [InlineData(4_590, "ZAR 45.90")]
    [InlineData(0, "ZAR 0.00")]
    public void Formats_consistently_regardless_of_host_culture(long minor, string expected) =>
        Assert.Equal(expected, new Money(minor, "zar").ToString());

    [Fact]
    public void Converts_major_units_without_floating_point_drift() =>
        Assert.Equal(-1_999, Money.FromMajor(-19.99m, "ZAR").AmountMinor);

    [Theory]
    [InlineData("")]
    [InlineData("RANDS")]
    [InlineData("Z4R")]
    public void Rejects_invalid_currency_codes(string currency) =>
        Assert.Throws<ArgumentException>(() => new Money(100, currency));

    [Fact]
    public void Is_a_value_object() =>
        Assert.Equal(new Money(100, "ZAR"), new Money(100, "zar"));
}

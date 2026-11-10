using DisputePortal.Application;
using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Application.Transactions;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.UnitTests.Application;

public sealed class OpenDisputeHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

    private readonly FakeTransactionRepository _transactions = new();
    private readonly FakeDisputeRepository _disputes = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Transaction _transaction;

    public OpenDisputeHandlerTests()
    {
        _transaction = new Transaction("customer-1", "TXN1", Now.AddDays(-2), "Corner Store", "Snacks", new Money(-2_500, "ZAR"), TransactionStatus.Posted, "4242");
        _transactions.Items.Add(_transaction);
    }

    private OpenDisputeHandler Handler() => new(
        _transactions, _disputes, new FakeUserDirectory(), _unitOfWork,
        new FakeDisputeReadModel(_disputes, _transactions), new DisputeEligibilityPolicy(120), new FixedClock(Now));

    private static OpenDisputeRequest Valid() =>
        new() { Reason = DisputeReason.UnrecognisedTransaction, Details = "I have never shopped at this merchant before." };

    [Fact]
    public async Task Opens_saves_and_returns_the_customer_view()
    {
        var detail = await Handler().HandleAsync(new OpenDisputeCommand("customer-1", _transaction.Id, Valid()));

        Assert.Single(_disputes.Items);
        Assert.Equal(1, _unitOfWork.Saves);
        Assert.Equal(DisputeStatus.Submitted, detail.Status);
        Assert.Equal("You", Assert.Single(detail.Timeline).Actor); // customer audience labels
    }

    [Fact]
    public async Task Someone_elses_transaction_is_not_found()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            Handler().HandleAsync(new OpenDisputeCommand("customer-2", _transaction.Id, Valid())));

        Assert.Equal("transaction.not_found", ex.Code);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_before_anything_is_loaded()
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Handler().HandleAsync(new OpenDisputeCommand("customer-1", _transaction.Id, new OpenDisputeRequest { Details = "short" })));

        Assert.Contains(nameof(OpenDisputeRequest.Reason), ex.Errors.Keys);
        Assert.Contains(nameof(OpenDisputeRequest.Details), ex.Errors.Keys);
        Assert.Empty(_disputes.Items);
    }

    [Fact]
    public async Task A_race_lost_at_the_database_becomes_a_friendly_conflict()
    {
        _unitOfWork.ThrowOnSave = new UniqueConstraintConflictException(new InvalidOperationException());

        var ex = await Assert.ThrowsAsync<DomainConflictException>(() =>
            Handler().HandleAsync(new OpenDisputeCommand("customer-1", _transaction.Id, Valid())));

        Assert.Equal("dispute.already_exists", ex.Code);
    }

    [Fact]
    public async Task Withdraw_reports_concurrent_edits_as_a_conflict()
    {
        await Handler().HandleAsync(new OpenDisputeCommand("customer-1", _transaction.Id, Valid()));
        _unitOfWork.ThrowOnSave = new ConcurrencyConflictException(new InvalidOperationException());
        var withdraw = new WithdrawDisputeHandler(_disputes, new FakeUserDirectory(), _unitOfWork,
            new FakeDisputeReadModel(_disputes, _transactions), new FixedClock(Now));

        var ex = await Assert.ThrowsAsync<DomainConflictException>(() =>
            withdraw.HandleAsync(new WithdrawDisputeCommand("customer-1", _disputes.Items[0].Id)));

        Assert.Equal("dispute.concurrent_update", ex.Code);
    }

    [Fact]
    public async Task Agents_see_real_names_customers_see_roles()
    {
        var opened = await Handler().HandleAsync(new OpenDisputeCommand("customer-1", _transaction.Id, Valid()));
        var readModel = new FakeDisputeReadModel(_disputes, _transactions);
        var startReview = new StartReviewHandler(_disputes, new FakeUserDirectory(), _unitOfWork, readModel, new FixedClock(Now));
        var resolve = new ResolveDisputeHandler(_disputes, new FakeUserDirectory(), _unitOfWork, readModel, new FixedClock(Now));

        await startReview.HandleAsync(new StartReviewCommand("agent-1", opened.Id));
        var agentView = await resolve.HandleAsync(new ResolveDisputeCommand("agent-1", opened.Id,
            new ResolveDisputeRequest { Outcome = DisputeOutcome.Declined, Note = "The charge is valid." }));
        var customerView = await new GetMyDisputeHandler(readModel).HandleAsync(new GetMyDisputeQuery("customer-1", opened.Id));

        Assert.Equal("Avery Agent", agentView.Timeline[^1].Actor);
        Assert.Equal("Avery Agent", agentView.ReviewerName);
        Assert.Equal(["You", "Disputes team", "Disputes team"], customerView.Timeline.Select(t => t.Actor));
        Assert.Null(customerView.ReviewerId);
        Assert.Null(customerView.ReviewerName);
    }

    [Fact]
    public async Task Only_the_reviewing_agent_can_resolve()
    {
        var opened = await Handler().HandleAsync(new OpenDisputeCommand("customer-1", _transaction.Id, Valid()));
        var readModel = new FakeDisputeReadModel(_disputes, _transactions);
        await new StartReviewHandler(_disputes, new FakeUserDirectory(), _unitOfWork, readModel, new FixedClock(Now))
            .HandleAsync(new StartReviewCommand("agent-1", opened.Id));
        var resolve = new ResolveDisputeHandler(_disputes, new FakeUserDirectory(), _unitOfWork, readModel, new FixedClock(Now));
        var saves = _unitOfWork.Saves;

        var ex = await Assert.ThrowsAsync<DomainConflictException>(() => resolve.HandleAsync(new ResolveDisputeCommand("agent-2", opened.Id,
            new ResolveDisputeRequest { Outcome = DisputeOutcome.Upheld, Note = "Refunded to your card." })));

        Assert.Equal("dispute.not_reviewer", ex.Code);
        Assert.Equal(saves, _unitOfWork.Saves);
    }
}

public sealed class ListTransactionsHandlerTests
{
    [Fact]
    public async Task Turns_local_whole_day_filters_into_a_half_open_utc_range_and_clamps_paging()
    {
        var readModel = new CapturingTransactionReadModel();
        var handler = new ListTransactionsHandler(readModel, new FixedClock(DateTime.UtcNow));

        await handler.HandleAsync(new ListTransactionsQuery("c", "  coffee  ", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), Page: 0, PageSize: 5000));

        var criteria = readModel.LastCriteria!;
        Assert.Equal("coffee", criteria.Search);
        Assert.Equal(new DateTime(2026, 10, 31, 22, 0, 0, DateTimeKind.Utc), criteria.FromUtc); // Johannesburg is UTC+2
        Assert.Equal(new DateTime(2026, 11, 1, 22, 0, 0, DateTimeKind.Utc), criteria.ToUtcExclusive);
        Assert.Equal((1, 100), (criteria.Page, criteria.PageSize));
    }
}

public sealed class DependencyInjectionTests
{
    [Fact]
    public void Every_use_case_handler_is_registered_by_convention()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddApplication();

        Assert.Contains(services, s => s.ServiceType == typeof(ICommandHandler<OpenDisputeCommand, DisputeDetail>));
        Assert.Contains(services, s => s.ServiceType == typeof(IQueryHandler<ListDisputeQueueQuery, PagedResult<DisputeSummary>>));
        Assert.Contains(services, s => s.ServiceType == typeof(IDomainEventHandler<DisputeOpened>));
    }
}

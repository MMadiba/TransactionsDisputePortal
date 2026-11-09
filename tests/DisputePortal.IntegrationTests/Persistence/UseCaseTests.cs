using DisputePortal.Application.Common;
using DisputePortal.Application.Contracts;
using DisputePortal.Application.Disputes;
using DisputePortal.Application.Transactions;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;
using DisputePortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DisputePortal.IntegrationTests.Persistence.PersistenceFixture;

namespace DisputePortal.IntegrationTests.Persistence;

public sealed class UseCaseTests : IAsyncLifetime
{
    private readonly PersistenceFixture _f = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _f.DisposeAsync();

    private static OpenDisputeRequest Valid(DisputeReason reason = DisputeReason.UnrecognisedTransaction) =>
        new() { Reason = reason, Details = "I have never shopped at this merchant before." };

    private Task<DisputeDetail> OpenAsync(Guid transactionId, string customerId = CustomerId) =>
        _f.SendAsync<OpenDisputeCommand, DisputeDetail>(new OpenDisputeCommand(customerId, transactionId, Valid()));

    private Task<DisputeDetail> WithdrawAsync(Guid disputeId) =>
        _f.SendAsync<WithdrawDisputeCommand, DisputeDetail>(new WithdrawDisputeCommand(CustomerId, disputeId));

    private Task<PagedResult<DisputeSummary>> HistoryAsync(DisputeFilter filter = DisputeFilter.All) =>
        _f.QueryAsync<ListMyDisputesQuery, PagedResult<DisputeSummary>>(new ListMyDisputesQuery(CustomerId, filter));

    [Fact]
    public async Task Opening_persists_the_aggregate_and_publishes_the_event_after_saving()
    {
        var tx = await _f.AddTransactionAsync();

        var detail = await OpenAsync(tx.Id);

        Assert.Equal(DisputeStatus.Submitted, detail.Status);
        Assert.Equal(detail.Id, Assert.Single(_f.Events.Received).DisputeId);
        await _f.InScopeAsync(async sp =>
        {
            var saved = await sp.GetRequiredService<ApplicationDbContext>().Disputes.Include(d => d.History).SingleAsync();
            Assert.Single(saved.History);
            Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
            return 0;
        });
    }

    [Fact]
    public async Task Customers_cannot_reach_each_others_data()
    {
        var theirs = await _f.AddTransactionAsync(customerId: OtherCustomerId);
        var theirDispute = await OpenAsync(theirs.Id, OtherCustomerId);

        await Assert.ThrowsAsync<NotFoundException>(() => OpenAsync(theirs.Id));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _f.QueryAsync<GetTransactionQuery, TransactionDetail>(new GetTransactionQuery(CustomerId, theirs.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _f.QueryAsync<GetMyDisputeQuery, DisputeDetail>(new GetMyDisputeQuery(CustomerId, theirDispute.Id)));
        await Assert.ThrowsAsync<NotFoundException>(() => WithdrawAsync(theirDispute.Id));
    }

    [Fact]
    public async Task One_live_dispute_per_transaction_until_withdrawn()
    {
        var tx = await _f.AddTransactionAsync();
        var first = await OpenAsync(tx.Id);

        Assert.Equal("dispute.already_exists", (await Assert.ThrowsAsync<DomainConflictException>(() => OpenAsync(tx.Id))).Code);

        await WithdrawAsync(first.Id);
        var second = await OpenAsync(tx.Id);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, (await HistoryAsync()).TotalCount);
    }

    [Fact]
    public async Task The_database_rejects_two_live_disputes_even_if_the_domain_check_is_bypassed()
    {
        var tx = await _f.AddTransactionAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => _f.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var policy = sp.GetRequiredService<DisputeEligibilityPolicy>();
            var customer = Actor.Customer(CustomerId, "Casey");
            db.Disputes.Add(Dispute.Open(tx, null, policy, DisputeReason.Other, "First of two racing requests.", customer, Now));
            db.Disputes.Add(Dispute.Open(tx, null, policy, DisputeReason.Other, "Second of two racing requests.", customer, Now));
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task History_filters_open_and_closed()
    {
        var a = await _f.AddTransactionAsync();
        var b = await _f.AddTransactionAsync();
        await OpenAsync(a.Id);
        await WithdrawAsync((await OpenAsync(b.Id)).Id);

        Assert.Equal(2, (await HistoryAsync()).TotalCount);
        Assert.Equal(a.Id, Assert.Single((await HistoryAsync(DisputeFilter.Open)).Items).TransactionId);
        Assert.Equal(DisputeStatus.Withdrawn, Assert.Single((await HistoryAsync(DisputeFilter.Closed)).Items).Status);
    }

    [Fact]
    public async Task Agent_workflow_and_audience_specific_timelines()
    {
        var tx = await _f.AddTransactionAsync();
        var opened = await OpenAsync(tx.Id);

        var queue = await _f.QueryAsync<ListDisputeQueueQuery, PagedResult<DisputeSummary>>(new ListDisputeQueueQuery());
        Assert.Equal("Casey Customer", Assert.Single(queue.Items).CustomerName);

        await _f.SendAsync<StartReviewCommand, DisputeDetail>(new StartReviewCommand(AgentId, opened.Id));
        var agentView = await _f.SendAsync<ResolveDisputeCommand, DisputeDetail>(new ResolveDisputeCommand(AgentId, opened.Id,
            new ResolveDisputeRequest { Outcome = DisputeOutcome.Upheld, Note = "Refunded to your card." }));

        var customerView = await _f.QueryAsync<GetMyDisputeQuery, DisputeDetail>(new GetMyDisputeQuery(CustomerId, opened.Id));

        Assert.Equal("Avery Agent", agentView.Timeline[^1].Actor);
        Assert.Equal(["You", "Disputes team", "Disputes team"], customerView.Timeline.Select(e => e.Actor));
        Assert.Equal(DisputeStatus.Upheld, customerView.Status);
        Assert.False(customerView.IsOpen);
    }

    [Fact]
    public async Task Transaction_search_paging_and_local_date_filters()
    {
        for (var i = 0; i < 25; i++)
        {
            await _f.AddTransactionAsync(daysAgo: i + 1, merchant: i % 5 == 0 ? "Book_Shop 100%" : "Corner Store");
        }

        Task<PagedResult<TransactionSummary>> List(ListTransactionsQuery q) =>
            _f.QueryAsync<ListTransactionsQuery, PagedResult<TransactionSummary>>(q);

        var page2 = await List(new ListTransactionsQuery(CustomerId, Page: 2, PageSize: 10));
        Assert.Equal((25, 10), (page2.TotalCount, page2.Items.Count));
        Assert.True(page2.Items.Zip(page2.Items.Skip(1)).All(p => p.First.PostedAtUtc >= p.Second.PostedAtUtc));

        // LIKE wildcards typed by the user are matched literally.
        Assert.Equal(5, (await List(new ListTransactionsQuery(CustomerId, Search: "_shop 100%"))).TotalCount);
        Assert.Equal(0, (await List(new ListTransactionsQuery(CustomerId, Search: "Store%"))).TotalCount);

        // Whole days in Africa/Johannesburg; each test transaction sits at 10:00 local time.
        var yesterday = DateOnly.FromDateTime(Now.AddHours(2)).AddDays(-1);
        Assert.Single((await List(new ListTransactionsQuery(CustomerId, From: yesterday, To: yesterday))).Items);
    }

    [Fact]
    public async Task Transaction_detail_explains_the_rule_that_blocks_a_dispute()
    {
        var credit = await _f.AddTransactionAsync(amountMinor: 50_000);
        var pending = await _f.AddTransactionAsync(status: TransactionStatus.Pending);

        var creditDetail = await _f.QueryAsync<GetTransactionQuery, TransactionDetail>(new GetTransactionQuery(CustomerId, credit.Id));
        var pendingDetail = await _f.QueryAsync<GetTransactionQuery, TransactionDetail>(new GetTransactionQuery(CustomerId, pending.Id));

        Assert.False(creditDetail.CanDispute);
        Assert.Contains("credit", creditDetail.CannotDisputeReason, StringComparison.Ordinal);
        Assert.Contains("Pending", pendingDetail.CannotDisputeReason, StringComparison.Ordinal);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Application.Contracts;
using DisputePortal.Domain.Disputes;
using DisputePortal.Domain.Transactions;

namespace DisputePortal.IntegrationTests.Web;

[Collection(PortalCollection.Name)]
public sealed class ApiTests(PortalFactory factory)
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(PortalFactory.Json))!;

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Finds a transaction for the user that satisfies the predicate (the seed data is random-but-deterministic).</summary>
    private static async Task<TransactionDetail> FindTransactionAsync(HttpClient client, Func<TransactionDetail, bool> predicate)
    {
        var page = await client.GetFromJsonAsync<PagedResult<TransactionSummary>>("/api/transactions?pageSize=100", PortalFactory.Json);
        foreach (var summary in page!.Items)
        {
            var detail = await client.GetFromJsonAsync<TransactionDetail>($"/api/transactions/{summary.Id}", PortalFactory.Json);
            if (predicate(detail!))
            {
                return detail!;
            }
        }

        throw new InvalidOperationException("No matching seeded transaction.");
    }

    private static readonly object ValidDispute = new { reason = "UnrecognisedTransaction", details = "I have never been to this merchant and still have my card." };

    [Fact]
    public async Task Calls_without_a_token_get_a_401_problem_response()
    {
        var response = await factory.CreateClient().GetAsync("/api/transactions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/token", new { email = PortalFactory.Sipho, password = "nope-nope-nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Customers_see_their_own_transactions_newest_first()
    {
        var client = await factory.ApiClientAsync(PortalFactory.Amanda);

        var page = await client.GetFromJsonAsync<PagedResult<TransactionSummary>>("/api/transactions?pageSize=50", PortalFactory.Json);

        Assert.NotNull(page);
        Assert.True(page.TotalCount > 40);
        Assert.Equal(50, page.Items.Count);
        Assert.True(page.Items.Zip(page.Items.Skip(1)).All(p => p.First.PostedAtUtc >= p.Second.PostedAtUtc));
        Assert.Contains(page.Items, t => t.LatestDispute is not null);
    }

    [Fact]
    public async Task Customers_cannot_see_another_customers_transaction()
    {
        var amanda = await factory.ApiClientAsync(PortalFactory.Amanda);
        var sipho = await factory.ApiClientAsync(PortalFactory.Sipho);
        var amandasTransaction = (await amanda.GetFromJsonAsync<PagedResult<TransactionSummary>>("/api/transactions", PortalFactory.Json))!.Items[0];

        var read = await sipho.GetAsync($"/api/transactions/{amandasTransaction.Id}");
        var dispute = await sipho.PostAsJsonAsync($"/api/transactions/{amandasTransaction.Id}/disputes", ValidDispute);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal("transaction.not_found", await ProblemCodeAsync(read));
        Assert.Equal(HttpStatusCode.NotFound, dispute.StatusCode);
    }

    [Fact]
    public async Task Full_customer_journey_dispute_view_history_withdraw()
    {
        var client = await factory.ApiClientAsync(PortalFactory.Sipho);
        var tx = await FindTransactionAsync(client, t => t.CanDispute && t.Merchant != "Kauai");

        // Raise it
        var created = await client.PostAsJsonAsync($"/api/transactions/{tx.Id}/disputes", ValidDispute);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dispute = await ReadAsync<DisputeDetail>(created);
        Assert.Equal(DisputeStatus.Submitted, dispute.Status);
        Assert.EndsWith($"/api/disputes/{dispute.Id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        // Can't raise it twice
        var duplicate = await client.PostAsJsonAsync($"/api/transactions/{tx.Id}/disputes", ValidDispute);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("dispute.already_exists", await ProblemCodeAsync(duplicate));

        // The transaction now reflects it
        var refreshed = await client.GetFromJsonAsync<TransactionDetail>($"/api/transactions/{tx.Id}", PortalFactory.Json);
        Assert.False(refreshed!.CanDispute);
        Assert.Equal(dispute.Id, Assert.Single(refreshed.Disputes).Id);

        // Withdraw, then it shows in closed history with a two-step timeline
        var withdrawn = await ReadAsync<DisputeDetail>(await client.PostAsync($"/api/disputes/{dispute.Id}/withdraw", null));
        Assert.Equal(DisputeStatus.Withdrawn, withdrawn.Status);
        Assert.Equal([DisputeStatus.Submitted, DisputeStatus.Withdrawn], withdrawn.Timeline.Select(e => e.Status));

        var closed = await client.GetFromJsonAsync<PagedResult<DisputeSummary>>("/api/disputes?filter=Closed", PortalFactory.Json);
        Assert.Contains(closed!.Items, d => d.Id == dispute.Id);

        var again = await client.PostAsync($"/api/disputes/{dispute.Id}/withdraw", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("dispute.not_open", await ProblemCodeAsync(again));
    }

    [Fact]
    public async Task Pending_transactions_are_rejected_with_422()
    {
        var client = await factory.ApiClientAsync(PortalFactory.Sipho);
        var pending = await FindTransactionAsync(client, t => t.Status == TransactionStatus.Pending);

        var response = await client.PostAsJsonAsync($"/api/transactions/{pending.Id}/disputes", ValidDispute);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("transaction.pending", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Invalid_input_returns_a_validation_problem()
    {
        var client = await factory.ApiClientAsync(PortalFactory.Sipho);
        var any = (await client.GetFromJsonAsync<PagedResult<TransactionSummary>>("/api/transactions", PortalFactory.Json))!.Items[0];

        var response = await client.PostAsJsonAsync($"/api/transactions/{any.Id}/disputes", new { details = "too short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Reason", out _));
        Assert.True(errors.TryGetProperty("Details", out _));
    }

    [Fact]
    public async Task Customers_cannot_use_the_agent_api()
    {
        var client = await factory.ApiClientAsync(PortalFactory.Amanda);

        var response = await client.GetAsync("/api/agent/disputes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agent_reviews_and_upholds_and_the_customer_sees_the_outcome()
    {
        var customer = await factory.ApiClientAsync(PortalFactory.Sipho);
        var agent = await factory.ApiClientAsync(PortalFactory.Agent);
        var tx = await FindTransactionAsync(customer, t => t.CanDispute && t.Merchant != "Kauai");
        var opened = await ReadAsync<DisputeDetail>(await customer.PostAsJsonAsync($"/api/transactions/{tx.Id}/disputes",
            new { reason = "DuplicateCharge", details = "The same amount came off twice within a minute." }));

        var queue = await agent.GetFromJsonAsync<PagedResult<DisputeSummary>>("/api/agent/disputes?filter=Open&pageSize=100", PortalFactory.Json);
        Assert.Contains(queue!.Items, d => d.Id == opened.Id && d.CustomerName == "Sipho Dlamini");

        Assert.Equal(HttpStatusCode.OK, (await agent.PostAsync($"/api/agent/disputes/{opened.Id}/start-review", null)).StatusCode);
        var resolved = await agent.PostAsJsonAsync($"/api/agent/disputes/{opened.Id}/resolve",
            new { outcome = "Upheld", note = "Confirmed duplicate. Refund issued." });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        var seenByCustomer = await customer.GetFromJsonAsync<DisputeDetail>($"/api/disputes/{opened.Id}", PortalFactory.Json);
        Assert.Equal(DisputeStatus.Upheld, seenByCustomer!.Status);
        Assert.Equal("Confirmed duplicate. Refund issued.", seenByCustomer.ResolutionNote);
        Assert.DoesNotContain(seenByCustomer.Timeline, e => e.Actor == "Naledi Khumalo");
    }

    [Fact]
    public async Task Seeded_history_contains_disputes_in_every_closed_state()
    {
        var client = await factory.ApiClientAsync(PortalFactory.Amanda);

        var history = await client.GetFromJsonAsync<PagedResult<DisputeSummary>>("/api/disputes", PortalFactory.Json);

        var statuses = history!.Items.Select(d => d.Status).ToHashSet();
        Assert.Superset(new HashSet<DisputeStatus> { DisputeStatus.Upheld, DisputeStatus.Declined, DisputeStatus.Withdrawn, DisputeStatus.UnderReview }, statuses);
    }
}

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisputePortal.IntegrationTests.Web;

[Collection(PortalCollection.Name)]
public sealed partial class WebsiteTests(PortalFactory factory)
{
    private HttpClient NoRedirectClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private static async Task<string> AntiforgeryTokenAsync(HttpResponseMessage page) =>
        AntiforgeryToken().Match(await page.Content.ReadAsStringAsync()).Groups[1].Value;

    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = NoRedirectClient();
        var loginPage = await client.GetAsync("/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = PortalFactory.Password,
            ["__RequestVerificationToken"] = await AntiforgeryTokenAsync(loginPage)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_api_and_not_the_website()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/transactions/{id}/disputes", json);
        Assert.Contains("/api/agent/disputes", json);
        Assert.DoesNotContain("\"/transactions\"", json);
    }

    [Fact]
    public async Task Pages_require_sign_in()
    {
        var response = await NoRedirectClient().GetAsync("/transactions");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Identity/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Self_registration_is_switched_off()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Identity/Account/Register")).StatusCode);
        Assert.DoesNotContain("Register", await client.GetStringAsync("/Identity/Account/Login"));
    }

    [Fact]
    public async Task Customer_can_sign_in_and_raise_a_dispute_through_the_website()
    {
        var client = await SignInAsync(PortalFactory.Amanda);

        var list = await client.GetAsync("/transactions?search=Kauai");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("Your transactions", await list.Content.ReadAsStringAsync());

        // Dispute one of the seeded duplicate Kauai charges.
        var (id, details) = await FindDisputableAsync(client, "/transactions?search=Kauai");

        var submit = await client.PostAsync($"/transactions/{id}/dispute", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Form.Reason"] = "DuplicateCharge",
            ["Form.Details"] = "I was charged twice for one lunch at Kauai.",
            ["__RequestVerificationToken"] = await AntiforgeryTokenAsync(details)
        }));

        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);
        var disputePage = await client.GetStringAsync(submit.Headers.Location!.OriginalString);
        Assert.Contains("we&#x27;ve received your dispute", disputePage);
        Assert.Contains("I was charged more than once", disputePage);
    }

    [Fact]
    public async Task Invalid_dispute_form_is_redisplayed_with_errors()
    {
        var client = await SignInAsync(PortalFactory.Amanda);
        var (id, details) = await FindDisputableAsync(client, "/transactions");

        var submit = await client.PostAsync($"/transactions/{id}/dispute", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Form.Details"] = "short",
            ["__RequestVerificationToken"] = await AntiforgeryTokenAsync(details)
        }));

        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var body = await submit.Content.ReadAsStringAsync();
        Assert.Contains("Tell us what went wrong.", body);
        Assert.Contains("short</textarea>", body); // what they typed is kept
    }

    /// <summary>Walks a transactions list page and returns the first transaction whose dispute form is on offer.</summary>
    private static async Task<(string Id, HttpResponseMessage DetailsPage)> FindDisputableAsync(HttpClient client, string listUrl)
    {
        var html = await client.GetStringAsync(listUrl);
        foreach (var id in Regex.Matches(html, "href=\"/transactions/([0-9a-f-]{36})\"").Select(m => m.Groups[1].Value).Distinct())
        {
            var details = await client.GetAsync($"/transactions/{id}");
            var body = await details.Content.ReadAsStringAsync();
            if (body.Contains("name=\"Form.Details\""))
            {
                return (id, details);
            }
        }

        throw new InvalidOperationException($"No disputable transaction on {listUrl}.");
    }

    [Fact]
    public async Task Forms_without_an_antiforgery_token_are_rejected()
    {
        var client = await SignInAsync(PortalFactory.Amanda);

        var response = await client.PostAsync($"/disputes/{Guid.NewGuid()}/withdraw", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customers_cannot_open_the_agent_queue()
    {
        var client = await SignInAsync(PortalFactory.Sipho);

        var response = await client.GetAsync("/agent/disputes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/ForgotPassword")]
    public async Task Pages_send_a_strict_content_security_policy(string path)
    {
        var response = await NoRedirectClient().GetAsync(path);

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.DoesNotContain("unsafe-inline", policy);

        // Everything the page loads must be allowed by that policy: local scripts only, nothing inline.
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/lib/jquery-validation/dist/jquery.validate.min", html);
        Assert.DoesNotContain("cdnjs", html);
        Assert.DoesNotMatch("<script>", html);
    }

    [Fact]
    public async Task Signed_out_visitors_go_straight_to_the_login_page()
    {
        var home = await NoRedirectClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/Identity/Account/Login", home.Headers.Location!.OriginalString, ignoreCase: true);
    }

    [Fact]
    public async Task Agent_lands_on_the_queue()
    {
        var client = await SignInAsync(PortalFactory.Agent);

        var home = await client.GetAsync("/");
        Assert.Equal("/agent/disputes", home.Headers.Location!.OriginalString, ignoreCase: true);
        Assert.Contains("Dispute queue", await client.GetStringAsync("/agent/disputes"));
    }
}

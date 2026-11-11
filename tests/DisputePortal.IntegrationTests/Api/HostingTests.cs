using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisputePortal.IntegrationTests.Api;

/// <summary>How the host serves the React app next to the API, and the headers and endpoints around them.</summary>
[Collection(PortalCollection.Name)]
public sealed class HostingTests(PortalFactory factory) : IDisposable
{
    // The real React build may not exist when the .NET tests run, so serve a stand-in index.html.
    private readonly string _webRoot = Directory.CreateTempSubdirectory("dp-webroot-").FullName;

    private HttpClient SiteClient()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Dispute Portal</title><div id=\"root\"></div>");
        return factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot))
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        try { Directory.Delete(_webRoot, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/transactions/0b9d2b5e-6c39-4c3b-9d3e-1f2a3b4c5d6e")]
    [InlineData("/agent/disputes/0b9d2b5e-6c39-4c3b-9d3e-1f2a3b4c5d6e")]
    public async Task Every_app_route_gets_the_react_app_so_deep_links_and_reloads_work(string path)
    {
        var response = await SiteClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<div id=\"root\">", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_api_routes_are_a_json_404_not_the_app()
    {
        var response = await SiteClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Responses_carry_a_strict_content_security_policy()
    {
        var response = await SiteClient().GetAsync("/");

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.DoesNotContain("unsafe-inline", policy);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task The_razor_site_and_identity_pages_are_gone()
    {
        // The old server-rendered sign-in page now just falls through to the React app, which has its own.
        var login = await SiteClient().GetAsync("/Identity/Account/Login");

        var html = await login.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"root\">", html);
        Assert.DoesNotContain("Input.Password", html);
    }

    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_api()
    {
        var json = JsonDocument.Parse(await factory.CreateClient().GetStringAsync("/openapi/v1.json"));
        var paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("/api/transactions/{id}/disputes", paths);
        Assert.Contains("/api/agent/disputes", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/", p));
    }
}

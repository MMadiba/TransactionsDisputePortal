using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DisputePortal.IntegrationTests.Api;

/// <summary>
/// Boots the real application (real pipeline, real SQLite file, real migrations, demo seed data)
/// in memory. One instance is shared by every test in the collection.
/// </summary>
public sealed class PortalFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test#Passw0rd";
    public const string SigningKey = "integration-test-signing-key-0123456789abcdef";
    public const string Amanda = "amanda.mitchell@mmbank.co.za";
    public const string Sipho = "sipho.dlamini@mmbank.co.za";
    public const string Ayanda = "ayanda.nkosi@mmbank.co.za";
    public const string Agent = "naledi.khumalo@mmbank.co.za";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"disputeportal-tests-{Guid.NewGuid():N}.db");
    private readonly ConcurrentDictionary<string, string> _tokens = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_databasePath};Pooling=False");
        builder.UseSetting("DemoData:Enabled", "true");
        builder.UseSetting("DemoData:Password", Password);
        builder.UseSetting("OpenApi:Enabled", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("RateLimiting:AuthenticationPerMinute", "1000");
    }

    /// <summary>An HTTP client carrying a bearer token for the given demo user (tokens are cached per user).</summary>
    public async Task<HttpClient> ApiClientAsync(string email)
    {
        var client = CreateClient();
        if (!_tokens.TryGetValue(email, out var token))
        {
            var response = await client.PostAsJsonAsync("/api/auth/token", new { email, password = Password });
            response.EnsureSuccessStatusCode();
            token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            _tokens[email] = token;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { File.Delete(_databasePath); } catch (IOException) { /* best effort */ }
    }
}

[CollectionDefinition(Name)]
public sealed class PortalCollection : ICollectionFixture<PortalFactory>
{
    public const string Name = "portal";
}

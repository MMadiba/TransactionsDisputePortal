using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DisputePortal.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using DisputePortal.Application.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.IntegrationTests.Api;

[Collection(PortalCollection.Name)]
public sealed class JwtAuthTests(PortalFactory factory)
{
    private async Task<AuthTokens> SignInAsync(string email = PortalFactory.Sipho)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/token", new { email, password = PortalFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(PortalFactory.Json))!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string? refreshToken) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private async Task<HttpStatusCode> CallApiAsync(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.GetAsync("/api/transactions?pageSize=1")).StatusCode;
    }

    [Fact]
    public async Task Token_is_a_signed_jwt_with_identity_roles_and_a_short_lifetime()
    {
        var tokens = await SignInAsync(PortalFactory.Amanda);

        Assert.Equal("Bearer", tokens.TokenType);
        Assert.Equal(15 * 60, tokens.ExpiresIn);
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        var jwt = new JsonWebToken(tokens.AccessToken);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal("dispute-portal", jwt.Issuer);
        Assert.Contains("dispute-portal-api", jwt.Audiences);
        Assert.Equal("Amanda Mitchell", jwt.GetClaim("name").Value);
        Assert.Equal("Customer", jwt.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(jwt.Subject));
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(tokens.AccessToken));
    }

    [Fact]
    public async Task Tampered_or_foreign_tokens_are_rejected()
    {
        var tokens = await SignInAsync();

        // Flip the role in the payload but keep the original signature.
        var parts = tokens.AccessToken.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1])).Replace("\"Customer\"", "\"Agent\"", StringComparison.Ordinal);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload))}.{parts[2]}";

        // A perfectly valid JWT, just signed with someone else's key.
        var foreign = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "dispute-portal",
            Audience = "dispute-portal-api",
            Claims = new Dictionary<string, object> { ["sub"] = "x", ["role"] = "Customer" },
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("an-attackers-own-key-that-is-long-enough!")), SecurityAlgorithms.HmacSha256)
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await CallApiAsync(tampered));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApiAsync(foreign));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallApiAsync("not-a-jwt"));
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_refresh_token_revokes_the_session()
    {
        var first = await SignInAsync();

        var rotated = await RefreshAsync(first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = (await rotated.Content.ReadFromJsonAsync<AuthTokens>(PortalFactory.Json))!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(second.AccessToken));

        // Replaying the first token looks like theft: it fails, and so does the legitimate newer one.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_refreshes_with_the_same_token_cannot_both_succeed()
    {
        var tokens = await SignInAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(tokens.RefreshToken)));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoked_refresh_tokens_cannot_be_used()
    {
        var tokens = await SignInAsync();

        var revoke = await factory.CreateClient().PostAsJsonAsync("/api/auth/revoke", new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Unknown_refresh_token_gets_a_problem_response()
    {
        var response = await RefreshAsync("definitely-not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Session expired", problem.GetProperty("title").GetString());
    }

    // ---------- Browser sign-in: the refresh token lives in an HttpOnly cookie ----------

    /// <summary>A client that keeps cookies, over HTTPS because the refresh cookie is Secure.</summary>
    private HttpClient BrowserClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task<AuthTokens> BrowserSignInAsync(HttpClient browser)
    {
        var response = await browser.PostAsJsonAsync("/api/auth/token", new { email = PortalFactory.Sipho, password = PortalFactory.Password, useCookie = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthApiController.RefreshCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(PortalFactory.Json))!;
    }

    [Fact]
    public async Task Browser_sign_in_puts_the_refresh_token_in_an_httponly_cookie_not_the_body()
    {
        var tokens = await BrowserSignInAsync(BrowserClient());

        Assert.Null(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(tokens.AccessToken));
    }

    [Fact]
    public async Task Browser_refresh_uses_and_rotates_the_cookie()
    {
        var browser = BrowserClient();
        await BrowserSignInAsync(browser);

        var first = await browser.PostAsync("/api/auth/refresh", null);
        var second = await browser.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // the cookie was rotated, so the second call used the new token
        var refreshed = (await second.Content.ReadFromJsonAsync<AuthTokens>(PortalFactory.Json))!;
        Assert.Null(refreshed.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, await CallApiAsync(refreshed.AccessToken));
    }

    [Fact]
    public async Task Browser_sign_out_revokes_and_clears_the_cookie()
    {
        var browser = BrowserClient();
        await BrowserSignInAsync(browser);

        var revoke = await browser.PostAsync("/api/auth/revoke", null);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_token_or_cookie_is_a_401()
    {
        var response = await BrowserClient().PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

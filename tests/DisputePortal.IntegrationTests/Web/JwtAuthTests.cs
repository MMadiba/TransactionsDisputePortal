using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DisputePortal.Application.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DisputePortal.IntegrationTests.Web;

[Collection(PortalCollection.Name)]
public sealed class JwtAuthTests(PortalFactory factory)
{
    private async Task<AuthTokens> SignInAsync(string email = PortalFactory.Sipho)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/token", new { email, password = PortalFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(PortalFactory.Json))!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
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
}

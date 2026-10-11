using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FinanceTests;

/// <summary>
/// Auth mode Gateway (Hestia T03): Hestia.Api signs users in, and finance accepts only its gateway tokens, and only from
/// the internal network. The tokens here are made as the host makes them (<see cref="TestGateway"/>).
/// </summary>
public class GatewayAuthTests
{
    /// <summary>A host in the auth mode Gateway. The connection comes from <paramref name="peer"/> (the hestia-api container).</summary>
    private static FinanceApiFactory Gateway(IPAddress? peer = null, IReadOnlyDictionary<string, string?>? more = null)
    {
        var settings = TestGateway.Settings();
        foreach (var (key, value) in more ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }
        return new FinanceApiFactory(
            services => services.AddSingleton<IStartupFilter>(new FromPeer(peer ?? TestGateway.Host)),
            signedIn: false,
            settings: settings);
    }

    private static HttpClient ClientWith(FinanceApiFactory factory, string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Fact]
    public async Task TheSignedInTests_UseTheAuthModeOfTheRun()
    {
        // CI runs all tests two times: with the test sign-in, and with gateway tokens (FINANCE_TEST_AUTH=gateway).
        await using var factory = new FinanceApiFactory();
        var client = factory.CreateClient();

        var mode = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FinanceApi.Auth.AuthOptions>>().Value.Mode;
        Assert.Equal(TestGateway.AllTests ? FinanceApi.Auth.AuthMode.Gateway : FinanceApi.Auth.AuthMode.Cookie, mode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Accounts")).StatusCode);
    }

    [Fact]
    public async Task AValidToken_OpensTheApi()
    {
        await using var factory = Gateway();
        var client = ClientWith(factory, TestGateway.Token());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Accounts")).StatusCode);
        var created = await client.PostAsJsonAsync("/api/Tags", new { name = "From the gateway" });
        Assert.True(created.IsSuccessStatusCode, $"POST /api/Tags answered {created.StatusCode}");
    }

    [Fact]
    public async Task WithoutAToken_Is401()
    {
        await using var factory = Gateway();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, null).GetAsync("/api/Accounts")).StatusCode);
    }

    [Fact]
    public async Task TheReadOnlyTokenOfFinance_AndTheSessionCookie_AreOff()
    {
        const string readOnlyToken = "a-read-only-token-long-enough-for-the-check";
        await using var factory = Gateway(more: new Dictionary<string, string?> { ["Auth:ReadOnlyToken"] = readOnlyToken });

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, readOnlyToken).GetAsync("/api/Accounts")).StatusCode);
        var withCookie = new HttpRequestMessage(HttpMethod.Get, "/api/Accounts");
        withCookie.Headers.Add("Cookie", "finance_session=anything");
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, null).SendAsync(withCookie)).StatusCode);
    }

    [Fact]
    public async Task OnlyTheHealthChecks_AreOpen_AndTheSignInEndpointsAreNotMapped()
    {
        await using var factory = Gateway();
        var client = ClientWith(factory, TestGateway.Token());
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Order()
            .ToList();
        Assert.Equal(["/health", "/health/live", "/health/ready"], anonymous);
        Assert.DoesNotContain(endpoints, e => e.RoutePattern.RawText!.Contains("Auth", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/Auth/login", new { username = "a", password = "b" })).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("not yet valid")]
    [InlineData("another key")]
    [InlineData("another issuer")]
    [InlineData("another audience")]
    [InlineData("no scope")]
    [InlineData("unknown scope")]
    [InlineData("HS256")]
    [InlineData("alg none")]
    [InlineData("changed payload")]
    public async Task AWrongToken_Is401(string problem)
    {
        await using var factory = Gateway();
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var token = problem switch
        {
            "expired" => TestGateway.Token(notBefore: DateTime.UtcNow.AddMinutes(-5)),
            "not yet valid" => TestGateway.Token(notBefore: DateTime.UtcNow.AddMinutes(5)),
            "another key" => TestGateway.Token(key: otherKey),
            "another issuer" => TestGateway.Token(issuer: "somebody"),
            "another audience" => TestGateway.Token(audience: "documents"),
            "no scope" => TestGateway.Token(withScope: false),
            "unknown scope" => TestGateway.Token(scope: "admin"),
            "HS256" => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = "hestia-api",
                Audience = "finance",
                Claims = new Dictionary<string, object> { ["sub"] = "user-1", ["scope"] = "full" },
                Expires = DateTime.UtcNow.AddMinutes(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestGateway.PublicKey)), SecurityAlgorithms.HmacSha256),
            }),
            "alg none" => Unsigned(TestGateway.Token()),
            _ => WithChangedPayload(TestGateway.Token(scope: "read-only")),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, token).GetAsync("/api/Accounts")).StatusCode);
    }

    [Fact]
    public async Task AValidToken_FromOutsideTheInternalNetwork_Is401()
    {
        // A machine on the internet or the tailnet, with a copied token: finance accepts tokens only from the host.
        await using var factory = Gateway(peer: IPAddress.Parse("203.0.113.7"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, TestGateway.Token()).GetAsync("/api/Accounts")).StatusCode);
    }

    [Fact]
    public async Task TheClientAddress_FromTheHost_DoesNotMatter_TheConnectionDoes()
    {
        // The host forwards the client address (a phone on the tailnet). The connection itself is the host.
        await using var factory = Gateway();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/Accounts");
        request.Headers.Add("X-Forwarded-For", "100.101.102.103");

        Assert.Equal(HttpStatusCode.OK, (await ClientWith(factory, TestGateway.Token()).SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task TheReadOnlyScope_ReadsAndQueries_AndChangesNothing()
    {
        await using var factory = Gateway();
        var client = ClientWith(factory, TestGateway.Token(scope: "read-only"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Transaction/Filter", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Budget/Filter", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/Tags", new { name = "Sneaky" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/Accounts", new { name = "Sneaky", type = "Chequing" })).StatusCode);
        var tags = await (await client.GetAsync("/api/Tags")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sneaky", tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not a key")]
    public async Task WithoutAValidPublicKey_TheApiDoesNotStart(string? key)
    {
        await using var factory = Gateway(more: new Dictionary<string, string?> { ["Auth:Gateway:PublicKey"] = key });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Auth:Gateway:PublicKey", error.ToString());
    }

    [Fact]
    public async Task CookieMode_IsTheDefault_AndDoesNotAcceptGatewayTokens()
    {
        await using var factory = new FinanceApiFactory(signedIn: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, TestGateway.Token()).GetAsync("/api/Accounts")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await factory.CreateClient().PostAsJsonAsync("/api/Auth/login", new { username = "a", password = "b" })).StatusCode);
    }

    /// <summary>The same header and claims with "alg": "none" and no signature.</summary>
    private static string Unsigned(string token)
    {
        var parts = token.Split('.');
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        return $"{header}.{parts[1]}.";
    }

    /// <summary>The scope changed to full after the signature (a forged token).</summary>
    private static string WithChangedPayload(string token)
    {
        var parts = token.Split('.');
        var payload = Base64UrlEncoder.Decode(parts[1]).Replace("\"read-only\"", "\"full\"");
        return $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";
    }
}

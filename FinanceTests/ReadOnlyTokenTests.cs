using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using FinanceApi.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinanceTests;

/// <summary>Auth:ReadOnlyToken: a bearer token that reads everything and changes nothing.</summary>
public class ReadOnlyTokenTests
{
    private const string Token = "a-read-only-token-long-enough-for-the-check";

    private static FinanceApiFactory WithToken(string? token = Token) =>
        new(signedIn: false, settings: new Dictionary<string, string?> { ["Auth:ReadOnlyToken"] = token });

    private static HttpClient ClientWith(FinanceApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Token_ReadsAccountsAndReports()
    {
        await using var factory = WithToken();
        var client = ClientWith(factory, Token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Reports/cash-flow")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Transaction/Filter", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Reports/breakdown", new { interval = "Month" })).StatusCode);
        Assert.Equal(ReadOnlyTokenHandler.Username, (await (await client.GetAsync("/api/Auth/me")).ReadAsync<CurrentUserDto>()).Username);
    }

    [Fact]
    public async Task Token_ChangesNothing()
    {
        await using var factory = WithToken();
        var client = ClientWith(factory, Token);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/Accounts", new { name = "Sneaky", type = "Chequing" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/Auth/change-password", new { currentPassword = "x", newPassword = "y" })).StatusCode);
        var accounts = await (await client.GetAsync("/api/Accounts")).ReadAsync<List<AccountOutputDto>>();
        Assert.DoesNotContain(accounts, a => a.Name == "Sneaky");
    }

    [Fact]
    public async Task Token_IsRefusedByEveryEndpointThatChangesData()
    {
        await using var factory = WithToken();
        var client = ClientWith(factory, Token);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();

        var queries = endpoints
            .Where(e => e.Metadata.GetMetadata<ReadOnlyQueryAttribute>() is not null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Order()
            .ToList();
        Assert.Equal(["/api/Budget/Filter", "/api/CategoryRules/Preview", "/api/Reports/breakdown", "/api/Reports/merchants",
            "/api/Transaction/Filter"], queries);

        foreach (var endpoint in endpoints)
        {
            var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                segment.IsSimple && segment.Parts[0] is RoutePatternLiteralPart literal ? literal.Content : Guid.NewGuid().ToString()));
            var readOnly = endpoint.Metadata.GetMetadata<ReadOnlyQueryAttribute>() is not null;
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });
                if (HttpMethods.IsGet(method) || readOnly)
                {
                    Assert.True(response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
                        $"{method} {path} answered {response.StatusCode}");
                }
                else
                {
                    Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} answered {response.StatusCode}");
                }
            }
        }
    }

    [Theory]
    [InlineData(Token + "x")]
    [InlineData("")]
    public async Task WrongToken_IsUnauthorized(string token)
    {
        await using var factory = WithToken();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, token).GetAsync("/api/Accounts")).StatusCode);
    }

    [Fact]
    public async Task WithoutAConfiguredToken_NoBearerTokenWorks()
    {
        await using var factory = WithToken(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(factory, Token).GetAsync("/api/Accounts")).StatusCode);
    }

    [Fact]
    public async Task AShortToken_StopsStartup()
    {
        await using var factory = WithToken("too-short");

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("at least 32 characters", error.Message);
    }
}

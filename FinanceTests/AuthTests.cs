using System.Net;
using System.Net.Http.Json;
using Finance.Contracts.Outputs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceTests;

/// <summary>The real cookie sign-in (the other tests use <see cref="TestSignIn"/>).</summary>
public class AuthTests
{
    private const string Username = "alex";
    private const string Password = "correct horse battery";

    private static async Task<FinanceApiFactory> WithAccountAsync()
    {
        var factory = new FinanceApiFactory(signedIn: false);
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.True((await users.CreateAsync(new IdentityUser(Username), Password)).Succeeded);
        return factory;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string password = Password, string username = Username) =>
        client.PostAsJsonAsync("/api/Auth/login", new { username, password });

    [Fact]
    public async Task OnlySignInAndHealthChecks_AreOpenToAnonymousUsers()
    {
        await using var factory = new FinanceApiFactory(signedIn: false);
        var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Order()
            .ToList();
        Assert.Equal(["/api/Auth/login", "/api/Auth/logout", "/health", "/health/live", "/health/ready"], anonymous);

        // And the rest really answer 401 without a session (auth runs before routing values or bodies are checked).
        foreach (var endpoint in endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                segment.IsSimple && segment.Parts[0] is Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal
                    ? literal.Content
                    : Guid.NewGuid().ToString()));
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
                {
                    Content = JsonContent.Create(new { })
                });
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} answered {response.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task Login_SetsAStrictHttpOnlyCookie_ThatOpensTheApiUntilLogout()
    {
        await using var factory = await WithAccountAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/me")).StatusCode);
        var login = await LoginAsync(client, username: "ALEX");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(Username, (await login.ReadAsync<CurrentUserDto>()).Username);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("finance_session="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        Assert.DoesNotContain("expires=", cookie.ToLowerInvariant()); // not "remember me": a session cookie

        Assert.Equal(Username, (await (await client.GetAsync("/api/Auth/me")).ReadAsync<CurrentUserDto>()).Username);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/Budget/Filter", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/Auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/Budget/Filter", new { })).StatusCode);
    }

    [Fact]
    public async Task RememberMe_MakesThePersistentCookie()
    {
        await using var factory = await WithAccountAsync();

        var login = await factory.CreateClient().PostAsJsonAsync("/api/Auth/login",
            new { username = Username, password = Password, rememberMe = true });

        Assert.Contains("expires=", login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("finance_session=")).ToLowerInvariant());
    }

    [Fact]
    public async Task WrongPasswordAndUnknownUser_GetTheSameAnswer()
    {
        await using var factory = await WithAccountAsync();
        var client = factory.CreateClient();

        var wrongPassword = await LoginAsync(client, "not the password");
        var unknownUser = await LoginAsync(client, username: "nobody");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync() is var a ? Detail(a) : null, Detail(await unknownUser.Content.ReadAsStringAsync()));
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie"));
    }

    private static string? Detail(string problemJson) =>
        System.Text.Json.JsonDocument.Parse(problemJson).RootElement.GetProperty("detail").GetString();

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_EvenForTheRightPassword()
    {
        await using var factory = await WithAccountAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++) await LoginAsync(client, "not the password");
        var locked = await LoginAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Auth/me")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ChecksTheCurrentOne_AndTheNewOneIsUsedFromThenOn()
    {
        await using var factory = await WithAccountAsync();
        var client = factory.CreateClient();
        await LoginAsync(client);
        const string newPassword = "a much longer passphrase";

        var wrongCurrent = await client.PostAsJsonAsync("/api/Auth/change-password", new { currentPassword = "nope", newPassword });
        var tooShort = await client.PostAsJsonAsync("/api/Auth/change-password", new { currentPassword = Password, newPassword = "short" });
        var changed = await client.PostAsJsonAsync("/api/Auth/change-password", new { currentPassword = Password, newPassword });

        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Contains("currentPassword", await wrongCurrent.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Auth/me")).StatusCode); // still signed in

        var other = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(other, newPassword)).StatusCode);
    }

    [Fact]
    public async Task InitialUser_IsCreatedAtStartup_AndCanBeReset()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:InitialUser:Username"] = Username,
            ["Auth:InitialUser:Password"] = Password,
        };
        await using var factory = new FinanceApiFactory(signedIn: false, settings: settings);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(factory.CreateClient())).StatusCode);

        // A second start with ResetPassword sets the configured password again (e.g. after it was forgotten).
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByNameAsync(Username))!;
            await users.ChangePasswordAsync(user, Password, "forgotten password here");
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        }
        var restarted = factory.WithWebHostBuilder(b => b.UseSetting("Auth:InitialUser:ResetPassword", "true"));

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(restarted.CreateClient())).StatusCode);
    }

    [Fact]
    public async Task InitialUser_WithAWeakPassword_StopsStartup()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:InitialUser:Username"] = Username,
            ["Auth:InitialUser:Password"] = "short",
        };
        await using var factory = new FinanceApiFactory(signedIn: false, settings: settings);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("at least 12 characters", error.Message);
    }
}

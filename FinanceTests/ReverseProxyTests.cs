using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceTests;

/// <summary>X-Forwarded-Proto from the reverse proxy decides whether the session cookie is Secure.</summary>
public class ReverseProxyTests
{
    [Theory]
    [InlineData("172.18.0.5", "https", true)]    // nginx container on a Docker network, TLS in front
    [InlineData("127.0.0.1", "https", true)]     // proxy on the same machine
    [InlineData("172.18.0.5", null, false)]      // plain HTTP all the way
    [InlineData("203.0.113.7", "https", false)]  // not a trusted proxy: its header is ignored
    public async Task SessionCookie_IsSecure_OnlyWhenATrustedProxySaysHttps(string clientAddress, string? forwardedProto, bool secure)
    {
        await using var factory = new FinanceApiFactory(
            services => services.AddSingleton<IStartupFilter>(new FromAddress(IPAddress.Parse(clientAddress))),
            signedIn: false);
        var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().CreateAsync(new IdentityUser("alex"), "correct horse battery");
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/login")
        {
            Content = JsonContent.Create(new { username = "alex", password = "correct horse battery" })
        };
        if (forwardedProto is not null) request.Headers.Add("X-Forwarded-Proto", forwardedProto);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("finance_session=")).ToLowerInvariant();
        Assert.Equal(secure, cookie.Contains("; secure"));
    }

    /// <summary>The test server has no network peer; this plays the part of the connecting client.</summary>
    private sealed class FromAddress(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

using Microsoft.AspNetCore.HttpOverrides;

namespace FinanceApi;

/// <summary>
/// The API runs behind the frontend's nginx (and possibly a TLS proxy in front of it). Their
/// X-Forwarded-Proto / X-Forwarded-For headers say how the browser really connected: HTTPS makes the session
/// cookie Secure. Only proxies on the networks in ReverseProxy:TrustedNetworks are believed, so a client
/// talking to the API directly can't claim to be something it isn't.
/// </summary>
public static class ReverseProxy
{
    public const string SectionName = "ReverseProxy";

    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = configuration.GetSection($"{SectionName}:TrustedNetworks").Get<string[]>() ?? [];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Loopback is trusted by default; add the configured networks (e.g. Docker's private ranges).
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }
}

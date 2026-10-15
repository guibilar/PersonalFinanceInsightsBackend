using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using FinanceApi.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinanceApi.Auth;

/// <summary>
/// Auth mode <c>Gateway</c> (Hestia T03, decision D5): Hestia.Api signs users in, and adds a short-lived token to each
/// request that it sends here. This API then accepts only that token:
/// <list type="bullet">
/// <item>a JWT, signed with ES256 by the host's private key; checked with the public key in <c>Auth:Gateway:PublicKey</c>,
/// or during a key change also <c>Auth:Gateway:NextPublicKey</c> (T43; the token's <c>kid</c> names the key);</item>
/// <item>issuer <c>hestia-api</c>, audience <c>finance</c>, and a valid lifetime (60 seconds, 10 seconds of clock skew);</item>
/// <item>only from the internal Docker network: the connection (not X-Forwarded-For) comes from a trusted proxy network
/// (<c>ReverseProxy:TrustedNetworks</c>) or loopback;</item>
/// <item>with a scope: <c>full</c>, or <c>read-only</c> (the host's read-only token). A read-only request can call GET and
/// the <see cref="ReadOnlyQueryAttribute"/> endpoints only (<see cref="ReadOnlyTokenHandler.MayProceed"/>).</item>
/// </list>
/// The session cookie, the read-only token, and <c>/api/Auth/*</c> are off in this mode: the host has them.
/// </summary>
public static class GatewayAuth
{
    public const string SchemeName = "Gateway";
    public const string ScopeClaim = "scope";
    public const string FullScope = "full";
    public const string ReadOnlyScope = "read-only";

    /// <summary>The address of the connection, before UseForwardedHeaders replaces it with the client's address.</summary>
    private const string PeerKey = "gateway-peer";

    public static void AddGatewayAuth(this AuthenticationBuilder authentication, IServiceCollection services, GatewayOptions gateway)
    {
        var keys = new List<ECDsaSecurityKey> { SecurityKey(LoadPublicKey(gateway.PublicKey, "Auth:Gateway:PublicKey")) };
        if (!string.IsNullOrWhiteSpace(gateway.NextPublicKey))
        {
            keys.Add(SecurityKey(LoadPublicKey(gateway.NextPublicKey, "Auth:Gateway:NextPublicKey")));
        }
        services.AddSingleton(new GatewayKeys(keys.Select(key => key.KeyId).ToList()));
        authentication
            .AddJwtBearer(SchemeName, options =>
            {
                // The claim names as the host writes them (sub, name, scope), not the long SOAP names.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = gateway.Issuer,
                    ValidAudience = gateway.Audience,
                    // A token with a kid gets the key with that id; a token without one (an older host) is tried
                    // with each key.
                    IssuerSigningKeys = keys,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(10),
                    NameClaimType = "name",
                };
                options.Events = new JwtBearerEvents
                {
                    // A token from outside the internal network is not looked at.
                    OnMessageReceived = context =>
                    {
                        if (!FromInternalNetwork(context.HttpContext))
                        {
                            context.NoResult();
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var scope = context.Principal?.FindFirstValue(ScopeClaim);
                        if (scope is not (FullScope or ReadOnlyScope))
                        {
                            context.Fail("The gateway token has no valid scope.");
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        // /api/Auth/* belongs to the host in this mode: the controller is not mapped.
        services.AddControllers().ConfigureApplicationPartManager(parts =>
            parts.FeatureProviders.Add(new WithoutAuthController()));
    }

    /// <summary>True for a read-only gateway user (the scope <c>read-only</c>).</summary>
    public static bool IsReadOnly(ClaimsIdentity identity) =>
        identity.AuthenticationType is not null && identity.HasClaim(ScopeClaim, ReadOnlyScope);

    /// <summary>
    /// Keeps the address of the connection for <see cref="FromInternalNetwork"/>. Put it before UseForwardedHeaders,
    /// which replaces it with the client's address from X-Forwarded-For.
    /// </summary>
    public static IApplicationBuilder UseGatewayPeer(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Items[PeerKey] = context.Connection.RemoteIpAddress;
            return next(context);
        });

    /// <summary>The connection comes from loopback or a trusted proxy network (the Docker network of the host).</summary>
    private static bool FromInternalNetwork(HttpContext context)
    {
        if (context.Items[PeerKey] is not IPAddress peer)
        {
            return false;
        }
        if (peer.IsIPv4MappedToIPv6)
        {
            peer = peer.MapToIPv4();
        }
        if (IPAddress.IsLoopback(peer))
        {
            return true;
        }
        var forwarded = context.RequestServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        return forwarded.KnownProxies.Contains(peer) || forwarded.KnownIPNetworks.Any(network => network.Contains(peer));
    }

    /// <summary>The key with its id: the JWK thumbprint (RFC 7638), the same id that the host writes as <c>kid</c>.</summary>
    private static ECDsaSecurityKey SecurityKey(ECDsa key)
    {
        var securityKey = new ECDsaSecurityKey(key);
        securityKey.KeyId = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey).ComputeJwkThumbprint());
        return securityKey;
    }

    private static ECDsa LoadPublicKey(string? value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Auth:Mode is Gateway, but {setting} is not set.");
        }
        var key = ECDsa.Create();
        try
        {
            var text = value.Trim();
            if (text.StartsWith("-----BEGIN", StringComparison.Ordinal))
            {
                key.ImportFromPem(text);
            }
            else
            {
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
            }
            if (key.KeySize != 256)
            {
                throw new CryptographicException("The key is not a P-256 key.");
            }
            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            key.Dispose();
            throw new InvalidOperationException(
                $"{setting} is not an ECDSA P-256 public key (PEM, or the DER bytes in base64).", exception);
        }
    }

    /// <summary>The ids of the accepted public keys (T43), for the start log: the first is the current key.</summary>
    public sealed record GatewayKeys(IReadOnlyList<string> KeyIds);

    /// <summary>Removes <see cref="AuthController"/> from the controllers.</summary>
    private sealed class WithoutAuthController : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.Where(c => c.AsType() == typeof(AuthController)).ToList())
            {
                feature.Controllers.Remove(controller);
            }
        }
    }
}

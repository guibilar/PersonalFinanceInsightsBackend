using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FinanceTests;

/// <summary>
/// Plays the part of Hestia.Api for the auth mode Gateway (Hestia T03): a signing key (a new one for each test run),
/// and gateway tokens as the host makes them (ES256, iss hestia-api, aud finance, sub, name, scope).
/// </summary>
public static class TestGateway
{
    /// <summary>FINANCE_TEST_AUTH=gateway: the signed-in feature tests use gateway tokens instead of <see cref="TestSignIn"/>.</summary>
    public const string Variable = "FINANCE_TEST_AUTH";

    public static bool AllTests =>
        string.Equals(Environment.GetEnvironmentVariable(Variable), "gateway", StringComparison.OrdinalIgnoreCase);

    /// <summary>The address of the hestia-api container on the Docker network "finance" (a trusted proxy network).</summary>
    public static readonly IPAddress Host = IPAddress.Parse("172.18.0.9");

    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public key, as in Auth:Gateway:PublicKey (the DER bytes in base64).</summary>
    public static string PublicKey { get; } = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    /// <summary>The settings of the auth mode Gateway with the test key.</summary>
    public static Dictionary<string, string?> Settings() => new()
    {
        ["Auth:Mode"] = "Gateway",
        ["Auth:Gateway:PublicKey"] = PublicKey,
    };

    /// <summary>A gateway token. By default: valid now, for 60 seconds, the full scope, signed with the test key.</summary>
    public static string Token(
        string scope = "full", string subject = "user-1", string name = "alex", TimeSpan? lifetime = null,
        DateTime? notBefore = null, string issuer = "hestia-api", string audience = "finance", ECDsa? key = null,
        bool withScope = true, bool withKeyId = true)
    {
        var start = notBefore ?? DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["name"] = name };
        if (withScope)
        {
            claims["scope"] = scope;
        }
        // As the host (T43): the kid is the JWK thumbprint of the public key.
        var securityKey = new ECDsaSecurityKey(key ?? Key);
        if (withKeyId)
        {
            securityKey.KeyId = KeyId(key ?? Key);
        }
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = start,
            NotBefore = start,
            Expires = start + (lifetime ?? TimeSpan.FromSeconds(60)),
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    /// <summary>The key id of a key: its JWK thumbprint (RFC 7638), as the host computes it.</summary>
    public static string KeyId(ECDsa key) => Base64UrlEncoder.Encode(
        JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(key)).ComputeJwkThumbprint());

    /// <summary>The public key of a key, as in the settings (the DER bytes in base64).</summary>
    public static string PublicKeyOf(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
}

/// <summary>The test server has no network peer. This plays the part of the connection (the host, or another machine).</summary>
public sealed class FromPeer(IPAddress address) : IStartupFilter
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

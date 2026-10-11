using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace FinanceApi.Auth;

/// <summary>
/// A token for scripts and assistants that only read: <c>Authorization: Bearer &lt;Auth:ReadOnlyToken&gt;</c>.
/// It opens every GET and the POSTs marked <see cref="ReadOnlyQueryAttribute"/> (searches whose criteria travel
/// in the body); anything else answers 403. Off until Auth:ReadOnlyToken is set.
/// </summary>
public class ReadOnlyTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    IOptionsMonitor<AuthOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ReadOnlyToken";
    public const string Username = "read-only token";
    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!HasBearerToken(Request))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string header = Request.Headers.Authorization!;
        var expected = auth.CurrentValue.ReadOnlyToken;
        if (string.IsNullOrEmpty(expected) || !SameToken(header[BearerPrefix.Length..].Trim(), expected))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown token."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, Username)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    public static bool HasBearerToken(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);

    // Hashing first gives equal lengths, so the comparison takes the same time whatever the guess.
    private static bool SameToken(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    /// <summary>
    /// True unless the only thing signing the request in is read-only (the read-only token, or a gateway token with the
    /// read-only scope, see <see cref="GatewayAuth"/>) and the request may change data.
    /// </summary>
    public static bool MayProceed(AuthorizationHandlerContext context)
    {
        if (context.User.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType != SchemeName && !GatewayAuth.IsReadOnly(i)))
        {
            return true;
        }
        return context.Resource is HttpContext http &&
               (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) ||
                http.GetEndpoint()?.Metadata.GetMetadata<ReadOnlyQueryAttribute>() is not null);
    }
}

/// <summary>A POST that only reads (its criteria are in the body), so the read-only token may call it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReadOnlyQueryAttribute : Attribute;

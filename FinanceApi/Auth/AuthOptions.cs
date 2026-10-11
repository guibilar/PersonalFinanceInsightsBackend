namespace FinanceApi.Auth;

/// <summary>Sign-in settings (section "Auth"). There is no public sign-up: accounts come from here.</summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Who signs users in. <see cref="AuthMode.Cookie"/> (the default): this API, with its own session cookie (standalone,
    /// and the tests). <see cref="AuthMode.Gateway"/>: Hestia.Api signs users in, and this API accepts only its gateway
    /// tokens (see <see cref="GatewayAuth"/>).
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.Cookie;

    /// <summary>The gateway token settings, used in <see cref="AuthMode.Gateway"/>.</summary>
    public GatewayOptions Gateway { get; set; } = new();

    public InitialUserOptions InitialUser { get; set; } = new();

    /// <summary>
    /// Opens the API read-only to <c>Authorization: Bearer &lt;token&gt;</c> (see <see cref="ReadOnlyTokenHandler"/>).
    /// At least <see cref="MinimumTokenLength"/> characters; unset turns it off.
    /// </summary>
    public string? ReadOnlyToken { get; set; }

    public const int MinimumTokenLength = 32;

    public bool IsValid() => string.IsNullOrEmpty(ReadOnlyToken) || ReadOnlyToken.Trim().Length >= MinimumTokenLength;
}

public enum AuthMode
{
    /// <summary>This API signs users in (the session cookie, the read-only token).</summary>
    Cookie,

    /// <summary>Hestia.Api signs users in. This API accepts only the gateway tokens of the host.</summary>
    Gateway
}

public class GatewayOptions
{
    /// <summary>
    /// The public key of the host's signing key (ECDSA P-256): PEM, or the DER bytes in base64 on one line.
    /// Required in <see cref="AuthMode.Gateway"/>.
    /// </summary>
    public string? PublicKey { get; set; }

    /// <summary>The <c>iss</c> of the tokens.</summary>
    public string Issuer { get; set; } = "hestia-api";

    /// <summary>The <c>aud</c> of the tokens.</summary>
    public string Audience { get; set; } = "finance";
}

public class InitialUserOptions
{
    /// <summary>Created at startup when no account with this name exists.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Also sets the password (and clears a lockout) when the account already exists. Remove it afterwards.</summary>
    public bool ResetPassword { get; set; }
}

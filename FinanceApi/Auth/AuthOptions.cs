namespace FinanceApi.Auth;

/// <summary>Sign-in settings (section "Auth"). There is no public sign-up: accounts come from here.</summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    public InitialUserOptions InitialUser { get; set; } = new();

    /// <summary>
    /// Opens the API read-only to <c>Authorization: Bearer &lt;token&gt;</c> (see <see cref="ReadOnlyTokenHandler"/>).
    /// At least <see cref="MinimumTokenLength"/> characters; unset turns it off.
    /// </summary>
    public string? ReadOnlyToken { get; set; }

    public const int MinimumTokenLength = 32;

    public bool IsValid() => string.IsNullOrEmpty(ReadOnlyToken) || ReadOnlyToken.Trim().Length >= MinimumTokenLength;
}

public class InitialUserOptions
{
    /// <summary>Created at startup when no account with this name exists.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Also sets the password (and clears a lockout) when the account already exists. Remove it afterwards.</summary>
    public bool ResetPassword { get; set; }
}

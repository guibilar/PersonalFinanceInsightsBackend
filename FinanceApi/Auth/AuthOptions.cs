namespace FinanceApi.Auth;

/// <summary>Sign-in settings (section "Auth"). There is no public sign-up: accounts come from here.</summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    public InitialUserOptions InitialUser { get; set; } = new();
}

public class InitialUserOptions
{
    /// <summary>Created at startup when no account with this name exists.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Also sets the password (and clears a lockout) when the account already exists. Remove it afterwards.</summary>
    public bool ResetPassword { get; set; }
}

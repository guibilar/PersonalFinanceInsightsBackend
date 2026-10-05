namespace Finance.Domain.Enums;

/// <summary>The Canadian registered plan a savings or investment account is held in, which limits what goes in.</summary>
public enum RegisteredPlan
{
    /// <summary>Not registered: no contribution limit.</summary>
    None,

    /// <summary>Tax-Free Savings Account: room comes each January; a withdrawal is room again the year after.</summary>
    Tfsa,

    /// <summary>Registered Retirement Savings Plan.</summary>
    Rrsp,

    /// <summary>First Home Savings Account.</summary>
    Fhsa,
}

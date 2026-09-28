namespace Finance.Domain.Enums;

/// <summary>What a transaction means for the reports, whichever account it is on.</summary>
public enum Flow
{
    /// <summary>Money out that counts as spending.</summary>
    Spending,

    /// <summary>Money back from a purchase: reduces spending.</summary>
    Refund,

    /// <summary>Money in that counts as income.</summary>
    Income,

    /// <summary>Money moving between your own accounts (paying the card, putting money into savings).</summary>
    Transfer,

    /// <summary>Not counted anywhere, e.g. a pending hold that shows up again once it goes through.</summary>
    Ignored
}

namespace Finance.Domain.Enums;

public static class AccountTypes
{
    /// <summary>
    /// Savings and investments: money is put away there, not spent. Their movements are transfers by default, their
    /// categories are left alone, and they make the cash-flow report's savings change.
    /// </summary>
    public static bool HoldsSavings(this AccountType type) => type is AccountType.Savings or AccountType.Investment;
}

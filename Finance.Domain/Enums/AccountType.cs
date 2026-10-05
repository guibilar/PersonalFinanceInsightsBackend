namespace Finance.Domain.Enums;

public enum AccountType
{
    Chequing,
    Savings,
    CreditCard,

    /// <summary>Investments (Wealthsimple, a brokerage): its value moves with the market, not only with its transactions.</summary>
    Investment,
}
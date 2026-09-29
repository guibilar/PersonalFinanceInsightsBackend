namespace Finance.Contracts.Inputs;

/// <summary>Longest text the API accepts (the frontend's inputs use the same limits).</summary>
public static class TextLimits
{
    public const int BudgetName = 100;
    public const int BudgetDescription = 500;
    public const int BudgetMerchant = 100;

    // Bank descriptions are well under this; room for a note added by hand.
    public const int TransactionDescription = 200;

    public const int CategoryRuleKeyword = 100;
    public const int FlowRuleKeyword = 100;
    public const int AccountName = 100;
    public const int CategoryName = 50;
}

namespace Finance.Domain.Enums;

/// <summary>Which linked transactions count towards a budget's spending.</summary>
public enum BudgetPeriod
{
    /// <summary>Everything ever linked (a trip, a project).</summary>
    Total,

    /// <summary>Only transactions dated in the current month; the budget starts over every month.</summary>
    Monthly
}

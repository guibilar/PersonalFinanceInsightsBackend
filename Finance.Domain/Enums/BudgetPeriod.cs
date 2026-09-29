namespace Finance.Domain.Enums;

/// <summary>Which transactions count towards a budget's spending, by their date.</summary>
public enum BudgetPeriod
{
    /// <summary>Everything ever linked (a trip, a project).</summary>
    Total,

    /// <summary>Only transactions dated in the current month; the budget starts over every month.</summary>
    Monthly,

    /// <summary>Only transactions dated in the current calendar year; the budget starts over every year.</summary>
    Yearly,

    /// <summary>Only transactions dated from the budget's start date to its end date, both included.</summary>
    Custom
}

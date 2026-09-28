namespace Finance.Contracts.Reports;

/// <summary>Income, spending and savings per month of the current year.</summary>
public class CashFlowReport
{
    /// <summary>Months with any transactions, oldest first.</summary>
    public List<CashFlowMonth> Months { get; set; } = [];

    // Averages over the complete months (before the current one); over every month when none is complete yet.
    public decimal AverageMonthlyIncome { get; set; }
    public decimal AverageMonthlySpending { get; set; }
    public decimal AverageMonthlySaved { get; set; }
}

public class CashFlowMonth
{
    /// <summary>"Jan 2026".</summary>
    public string Month { get; set; } = string.Empty;
    public decimal Income { get; set; }

    /// <summary>Spending minus refunds, as in the spending reports.</summary>
    public decimal Spending { get; set; }

    /// <summary>Income minus spending.</summary>
    public decimal Net { get; set; }

    /// <summary>Money into savings accounts minus money out of them.</summary>
    public decimal Saved { get; set; }
}

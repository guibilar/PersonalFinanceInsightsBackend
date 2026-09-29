namespace Finance.Contracts.Reports;

/// <summary>Income, spending, what was left over and what happened to savings, per month of the current year.</summary>
public class CashFlowReport
{
    /// <summary>Months with anything that counts, oldest first.</summary>
    public List<CashFlowMonth> Months { get; set; } = [];

    // Averages over the complete months (before the current one); over every month when none is complete yet.
    public decimal AverageMonthlyIncome { get; set; }
    public decimal AverageMonthlySpending { get; set; }

    /// <summary>Income minus spending, per month: <see cref="AverageMonthlyIncome"/> minus <see cref="AverageMonthlySpending"/>.</summary>
    public decimal AverageMonthlyNet { get; set; }

    public decimal AverageMonthlySavingsChange { get; set; }

    /// <summary>How many months the averages are over.</summary>
    public int AveragedMonths { get; set; }

    /// <summary>True when no month is over yet, so the running one is what the averages are over.</summary>
    public bool AveragesIncludeCurrentMonth { get; set; }
}

public class CashFlowMonth
{
    /// <summary>"Jan 2026".</summary>
    public string Month { get; set; } = string.Empty;

    /// <summary>Money in that counts as income (not transfers, refunds or ignored rows).</summary>
    public decimal Income { get; set; }

    /// <summary>Spending minus refunds, as in the spending reports.</summary>
    public decimal Spending { get; set; }

    /// <summary>What was left over: <see cref="Income"/> minus <see cref="Spending"/>. Negative when more went out than came in.</summary>
    public decimal Net { get; set; }

    /// <summary>
    /// How the savings accounts changed: money into them minus money out of them (interest included), whatever it
    /// was. 0 when nothing was recorded on a savings account, which is not the same as nothing being saved.
    /// </summary>
    public decimal SavingsChange { get; set; }
}

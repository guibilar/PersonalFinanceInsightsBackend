namespace Finance.Contracts.Reports;

/// <summary>How long each period of a breakdown is. Weeks start on Monday.</summary>
public enum BreakdownInterval
{
    Week,
    Month,
    Quarter,
    Year
}

/// <summary>
/// Totals for the transactions a filter picks (the transaction list's), and how they spread over time: one entry per
/// period from the first to the last, empty ones included, with running totals.
/// </summary>
public class TransactionBreakdown
{
    public BreakdownInterval Interval { get; set; }

    /// <summary>Every matching transaction, whatever it counts as.</summary>
    public int Count { get; set; }

    /// <summary>Spending minus refunds, as in the spending reports.</summary>
    public decimal Spending { get; set; }

    /// <summary>The refunds already taken off <see cref="Spending"/>.</summary>
    public decimal Refunds { get; set; }

    public decimal Income { get; set; }

    /// <summary>
    /// <see cref="Spending"/> per period, over the periods that are over (the running one would pull it down); over all
    /// of them when none is over yet.
    /// </summary>
    public decimal AverageSpending { get; set; }

    public decimal AverageIncome { get; set; }

    /// <summary>How many periods the averages are over.</summary>
    public int AveragedPeriods { get; set; }

    /// <summary>Oldest first, each period once, empty ones included.</summary>
    public List<BreakdownPeriod> Periods { get; set; } = [];

    /// <summary>Where the spending went, largest first (at most eight).</summary>
    public List<MerchantTotal> TopMerchants { get; set; } = [];
}

public class BreakdownPeriod
{
    /// <summary>First day.</summary>
    public DateTime Start { get; set; }

    /// <summary>Last day (inclusive).</summary>
    public DateTime End { get; set; }

    /// <summary>"Sep 1" for a week (its Monday), "Sep 2026", "Q3 2026", "2026".</summary>
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }
    public decimal Spending { get; set; }
    public decimal Income { get; set; }

    /// <summary>Spending from the first period up to and including this one.</summary>
    public decimal CumulativeSpending { get; set; }

    public decimal CumulativeIncome { get; set; }
}

/// <summary>
/// Spending at one merchant: the category rule keyword that matched the descriptions (the rules name merchants), or
/// the description itself when no rule matched.
/// </summary>
public class MerchantTotal
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Spending { get; set; }
}

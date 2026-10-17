using Finance.Contracts.Outputs;
using Finance.Domain.Enums;

namespace Finance.Contracts.Reports;

/// <summary>How often a recurring payment comes.</summary>
public enum RecurringCadence
{
    Weekly,
    Biweekly,
    Monthly,
    Quarterly,
    Yearly
}

/// <summary>Whether a recurring payment still comes when expected.</summary>
public enum RecurringStatus
{
    /// <summary>The last charge came on schedule; the next is due on <see cref="RecurringPayment.NextDate"/>.</summary>
    Active,

    /// <summary>The next charge is past due, but not by a whole period: missed, or just late.</summary>
    Late,

    /// <summary>No charge for more than a period after one was due: cancelled, most likely.</summary>
    Stopped
}

/// <summary>Payments that come again and again (subscriptions, bills, rent), found in the spending.</summary>
public class RecurringReport
{
    /// <summary>The ones still coming (active and late) by what they cost a month, then the stopped ones, latest first.</summary>
    public List<RecurringPayment> Payments { get; set; } = [];

    /// <summary>What the active and late ones cost a month, each at its own cadence.</summary>
    public decimal MonthlyTotal { get; set; }

    /// <summary><see cref="MonthlyTotal"/> over a year.</summary>
    public decimal YearlyTotal { get; set; }
}

public class RecurringPayment
{
    /// <summary>The merchant: its category rule's keyword, or the start of its description ("NETFLIX.COM").</summary>
    public string Name { get; set; } = string.Empty;

    public RecurringCadence Cadence { get; set; }
    public RecurringStatus Status { get; set; }

    /// <summary>A typical charge: the latest when the price is fixed, the average of the last three when it varies.</summary>
    public decimal Amount { get; set; }

    /// <summary>The charges differ from one to the next (a utility bill, say).</summary>
    public bool VariableAmount { get; set; }

    public DateTime FirstDate { get; set; }
    public DateTime LastDate { get; set; }
    public decimal LastAmount { get; set; }

    /// <summary>When the next charge is due: after <see cref="LastDate"/> by the cadence.</summary>
    public DateTime NextDate { get; set; }

    /// <summary>How many charges were found.</summary>
    public int Count { get; set; }

    /// <summary><see cref="Amount"/> a month, at its cadence.</summary>
    public decimal MonthlyCost { get; set; }

    /// <summary><see cref="Amount"/> a year, at its cadence.</summary>
    public decimal YearlyCost { get; set; }

    /// <summary>The category of the latest charge.</summary>
    public CategoryReferenceDto? Category { get; set; }

    /// <summary>A fixed price that changed with the latest charge; null otherwise.</summary>
    public PriceChange? PriceChange { get; set; }
}

public class PriceChange
{
    public decimal From { get; set; }
    public decimal To { get; set; }

    /// <summary>The first charge at the new price.</summary>
    public DateTime Since { get; set; }
}

/// <summary>Where this month's spending is heading, and each monthly and yearly budget with it.</summary>
public class SpendingForecast
{
    /// <summary>"October 2026".</summary>
    public string Month { get; set; } = string.Empty;

    public int DaysInMonth { get; set; }

    /// <summary>Days after today until the month ends.</summary>
    public int DaysLeft { get; set; }

    /// <summary>Spent so far this month (refunds taken off).</summary>
    public decimal Spent { get; set; }

    /// <summary>Where the month should end: <see cref="Spent"/>, the recurring payments still due, and usual day-to-day spending for the days left.</summary>
    public decimal Projected { get; set; }

    /// <summary>The average of the last complete months (up to three); null with no complete month yet.</summary>
    public decimal? Usual { get; set; }

    /// <summary>How many months <see cref="Usual"/> is over.</summary>
    public int UsualMonths { get; set; }

    /// <summary>Day-to-day spending a day, recurring payments left out, that the days left are projected at.</summary>
    public decimal DailyRate { get; set; }

    /// <summary>The recurring payments expected before the month ends, soonest first.</summary>
    public List<UpcomingCharge> Upcoming { get; set; } = [];

    /// <summary>Active monthly and yearly budgets that count by category or merchant, fullest projection first.</summary>
    public List<BudgetForecast> Budgets { get; set; } = [];
}

public class UpcomingCharge
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}

public class BudgetForecast
{
    public Guid BudgetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public BudgetPeriod Period { get; set; }
    public decimal Amount { get; set; }
    public decimal Spent { get; set; }

    /// <summary>Where it should end, this month or this year (by its period), at its usual pace.</summary>
    public decimal Projected { get; set; }

    public decimal ProjectedPercentage { get; set; }
}

/// <summary>A merchant whose spending landed in Other because no rule names it: a rule would sort them all at once.</summary>
public class UncategorisedMerchant
{
    /// <summary>The rule keyword to suggest: the start of the descriptions, upper case ("CORNER STORE").</summary>
    public string Keyword { get; set; } = string.Empty;

    public int Count { get; set; }
    public decimal Spending { get; set; }

    /// <summary>Up to three of its descriptions, as the bank wrote them.</summary>
    public List<string> Examples { get; set; } = [];

    public DateTime LastDate { get; set; }
}

public class UncategorisedReport
{
    /// <summary>Most transactions first (at most fifty).</summary>
    public List<UncategorisedMerchant> Merchants { get; set; } = [];

    /// <summary>Every transaction left in Other that a rule could sort, across all of them.</summary>
    public int TransactionCount { get; set; }
}

/// <summary>A month or a year looked back on: the totals against the period before, where the money went, and highlights.</summary>
public class ReviewReport
{
    /// <summary>"September 2026", or "2026".</summary>
    public string Label { get; set; } = string.Empty;

    public int Year { get; set; }

    /// <summary>Null for a year's review.</summary>
    public int? Month { get; set; }

    /// <summary>First day; <see cref="End"/> is the last (inclusive).</summary>
    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    /// <summary>False while the period is still running: its totals are so far.</summary>
    public bool Complete { get; set; }

    /// <summary>"August 2026", or "2025".</summary>
    public string PreviousLabel { get; set; } = string.Empty;

    /// <summary>
    /// Last day (inclusive) the previous totals count: the previous period's end once this one is over, the same day
    /// of it while this one still runs.
    /// </summary>
    public DateTime PreviousEnd { get; set; }

    public decimal Spending { get; set; }
    public decimal PreviousSpending { get; set; }
    public decimal Income { get; set; }
    public decimal PreviousIncome { get; set; }

    /// <summary>Spending transactions (refunds aside).</summary>
    public int Purchases { get; set; }

    /// <summary>Days (up to today, and from the first transaction stored) without a purchase.</summary>
    public int NoSpendDays { get; set; }

    /// <summary>Every category spent in, now or in the period before, by spending now.</summary>
    public List<ReviewCategory> Categories { get; set; } = [];

    /// <summary>Where the most went (at most five).</summary>
    public List<MerchantTotal> TopMerchants { get; set; } = [];

    /// <summary>The largest single purchase; null with none.</summary>
    public ReviewPurchase? BiggestPurchase { get; set; }

    /// <summary>For a year: each month up to now, January first. Empty for a month.</summary>
    public List<ReviewMonth> Months { get; set; } = [];

    /// <summary>The recurring payments still coming, and what they cost a month, as of today.</summary>
    public int RecurringCount { get; set; }

    public decimal RecurringMonthlyTotal { get; set; }

    /// <summary>The month of the first transaction there is ("2025-03"), so a review can't go further back; null with none.</summary>
    public string? Earliest { get; set; }
}

public class ReviewCategory
{
    public CategoryReferenceDto Category { get; set; } = null!;
    public decimal Spending { get; set; }
    public decimal PreviousSpending { get; set; }
}

public class ReviewPurchase
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public CategoryReferenceDto? Category { get; set; }
}

public class ReviewMonth
{
    /// <summary>"Jan".</summary>
    public string Label { get; set; } = string.Empty;

    public decimal Spending { get; set; }
    public decimal Income { get; set; }
}

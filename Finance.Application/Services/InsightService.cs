using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Import;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

/// <summary>What the spending says on its own: recurring payments, where the month is heading, what no rule sorts yet, and reviews.</summary>
public class InsightService(
    ITransactionRepository transactionRepository,
    ICategoryRuleRepository categoryRuleRepository,
    IBudgetRepository budgetRepository,
    ReportingCalendar calendar) : IInsightService
{
    /// <summary>Two years and a month: a yearly payment needs two charges to be seen.</summary>
    private const int RecurringLookBackMonths = 25;

    /// <summary>Complete months that "usual" is over.</summary>
    private const int UsualMonths = 3;

    private const int MaxUncategorised = 50;
    private const int TopMerchants = 5;

    public async Task<RecurringReport> GetRecurringAsync(CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        var detected = await DetectRecurringAsync(await LoadAsync(RecurringStart(today), today.AddDays(1), cancellationToken), today, cancellationToken);
        var coming = detected.Select(d => d.Payment).Where(p => p.Status != RecurringStatus.Stopped).ToList();
        var monthly = coming.Sum(p => p.MonthlyCost);
        return new RecurringReport
        {
            Payments = coming.OrderByDescending(p => p.MonthlyCost).ThenBy(p => p.Name, StringComparer.Ordinal)
                .Concat(detected.Select(d => d.Payment).Where(p => p.Status == RecurringStatus.Stopped).OrderByDescending(p => p.LastDate))
                .ToList(),
            MonthlyTotal = monthly,
            YearlyTotal = coming.Sum(p => p.YearlyCost),
        };
    }

    public async Task<SpendingForecast> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        var monthStart = calendar.CurrentMonthStart();
        var nextMonth = monthStart.AddMonths(1);
        var monthEnd = nextMonth.AddDays(-1);
        var daysLeft = (monthEnd - today).Days;

        var transactions = await LoadAsync(RecurringStart(today), nextMonth, cancellationToken);
        var detected = await DetectRecurringAsync(transactions, today, cancellationToken);
        var recurring = detected.SelectMany(d => d.Charges).ToHashSet();
        var thisMonth = transactions.Where(t => t.Date >= monthStart).ToList();
        var spent = thisMonth.Sum(MonthlyRecordsCalculator.SpendingAmount);

        var upcoming = detected
            .Where(d => d.Payment.Status != RecurringStatus.Stopped)
            .SelectMany(d => Occurrences(d.Payment, today, monthStart, monthEnd)
                .Select(date => new UpcomingCharge { Name = d.Payment.Name, Amount = d.Payment.Amount, Date = date }))
            .OrderBy(c => c.Date).ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        // The months before this one that are wholly in the data: the first, when it starts part-way, is left out.
        var history = HistoryMonths(transactions, monthStart);
        var historyDays = history.Sum(m => DateTime.DaysInMonth(m.Year, m.Month));
        bool InHistory(Transaction t) => history.Count > 0 && t.Date >= history[0] && t.Date < monthStart;
        var dailyRate = historyDays > 0
            ? transactions.Where(t => InHistory(t) && !recurring.Contains(t.Id)).Sum(MonthlyRecordsCalculator.SpendingAmount) / historyDays
            // No complete month yet: this month's pace.
            : thisMonth.Where(t => !recurring.Contains(t.Id)).Sum(MonthlyRecordsCalculator.SpendingAmount) / today.Day;
        dailyRate = Math.Max(0, dailyRate);

        var budgets = await budgetRepository.FilterAsync(
            new BudgetFilterDto { Status = Status.Active, Page = 1, PageSize = PagingLimits.MaxPageSize }, today, cancellationToken);
        return new SpendingForecast
        {
            Month = monthStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            DaysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month),
            DaysLeft = daysLeft,
            Spent = spent,
            Projected = Math.Round(spent + upcoming.Sum(c => c.Amount) + dailyRate * daysLeft, 2),
            Usual = history.Count > 0
                ? Math.Round(transactions.Where(InHistory).Sum(MonthlyRecordsCalculator.SpendingAmount) / history.Count, 2)
                : null,
            UsualMonths = history.Count,
            DailyRate = Math.Round(dailyRate, 2),
            Upcoming = upcoming,
            Budgets = budgets.Items
                .Select(b => BudgetForecast(b, transactions, history, historyDays, today))
                .OfType<BudgetForecast>()
                .OrderByDescending(b => b.ProjectedPercentage)
                .ToList(),
        };
    }

    public async Task<UncategorisedReport> GetUncategorisedAsync(CancellationToken cancellationToken = default)
    {
        var matcher = await MatcherAsync(cancellationToken);
        // Left in Other by the rules (not picked by hand), and not savings, whose movements stay in Other on purpose.
        // A rule that names Other on purpose isn't missing.
        var unsorted = (await transactionRepository.FilterNoPaginationAsync(new TransactionFilterDto { CategoryId = Category.OtherId }, cancellationToken))
            .Where(t => !t.CategoryLocked && !t.Account!.Type.HoldsSavings() && t.Flow is Flow.Spending or Flow.Refund
                        && matcher.MatchingKeyword(t.Description) is null)
            .ToList();
        return new UncategorisedReport
        {
            TransactionCount = unsorted.Count,
            Merchants = unsorted
                .GroupBy(t => MerchantNames.Normalize(t.Description))
                .Select(g => new UncategorisedMerchant
                {
                    Keyword = g.Key,
                    Count = g.Count(),
                    Spending = g.Sum(MonthlyRecordsCalculator.SpendingAmount),
                    Examples = g.OrderByDescending(t => t.Date).Select(t => t.Description).Distinct().Take(3).ToList(),
                    LastDate = g.Max(t => t.Date),
                })
                .OrderByDescending(m => m.Count).ThenByDescending(m => m.Spending).ThenBy(m => m.Keyword, StringComparer.Ordinal)
                .Take(MaxUncategorised)
                .ToList(),
        };
    }

    public async Task<ReviewReport> GetReviewAsync(int? year, int? month, CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        if (year is null)
        {
            // The last month that is over.
            var last = calendar.CurrentMonthStart().AddMonths(-1);
            (year, month) = (last.Year, last.Month);
        }
        if (year < 2000 || year > today.Year) throw new InvalidInputException("year", $"Choose a year from 2000 to {today.Year}.");
        if (month is < 1 or > 12) throw new InvalidInputException("month", "Choose a month from 1 to 12.");

        var start = new DateTime(year.Value, month ?? 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (start > today) throw new InvalidInputException("month", "That month hasn't started yet.");
        var end = month is null ? start.AddYears(1) : start.AddMonths(1);
        var previousStart = month is null ? start.AddYears(-1) : start.AddMonths(-1);
        string Label(DateTime date) => month is null
            ? date.Year.ToString(CultureInfo.InvariantCulture)
            : date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        var matcher = await MatcherAsync(cancellationToken);
        var both = await LoadAsync(previousStart, end, cancellationToken);
        var current = both.Where(t => t.Date >= start).ToList();
        var previous = both.Where(t => t.Date < start).ToList();
        var purchases = current.Where(t => t.Flow == Flow.Spending).ToList();

        var lastDay = Min(end.AddDays(-1), today);
        var days = lastDay >= start ? (lastDay - start).Days + 1 : 0;
        var spendDays = purchases.Where(t => t.Date <= lastDay).Select(t => t.Date.Date).Distinct().Count();

        var recurring = (await DetectRecurringAsync(await LoadAsync(RecurringStart(today), today.AddDays(1), cancellationToken), today, cancellationToken))
            .Select(d => d.Payment).Where(p => p.Status != RecurringStatus.Stopped).ToList();
        var earliest = await transactionRepository.FilterAsync(
            new TransactionFilterDto { SortBy = TransactionSortField.Date, SortDirection = SortDirection.Ascending, Page = 1, PageSize = 1 },
            cancellationToken);

        var biggest = purchases.OrderByDescending(t => t.Amount).ThenBy(t => t.Date).FirstOrDefault();
        return new ReviewReport
        {
            Label = Label(start),
            Year = year.Value,
            Month = month,
            Start = start,
            End = end.AddDays(-1),
            Complete = today >= end,
            PreviousLabel = Label(previousStart),
            Spending = current.Sum(MonthlyRecordsCalculator.SpendingAmount),
            PreviousSpending = previous.Sum(MonthlyRecordsCalculator.SpendingAmount),
            Income = current.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount),
            PreviousIncome = previous.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount),
            Purchases = purchases.Count,
            NoSpendDays = days - spendDays,
            Categories = both
                .Where(t => t.Flow is Flow.Spending or Flow.Refund)
                .GroupBy(t => t.CategoryId)
                .Select(g => new ReviewCategory
                {
                    Category = Reference(g.First().Category!),
                    Spending = g.Where(t => t.Date >= start).Sum(MonthlyRecordsCalculator.SpendingAmount),
                    PreviousSpending = g.Where(t => t.Date < start).Sum(MonthlyRecordsCalculator.SpendingAmount),
                })
                .Where(c => c.Spending != 0 || c.PreviousSpending != 0)
                .OrderByDescending(c => c.Spending).ThenByDescending(c => c.PreviousSpending).ThenBy(c => c.Category.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TopMerchants = current
                .Where(t => t.Flow is Flow.Spending or Flow.Refund)
                .GroupBy(t => MerchantNames.Of(matcher, t.Description))
                .Select(g => new MerchantTotal { Name = g.Key, Count = g.Count(), Spending = g.Sum(MonthlyRecordsCalculator.SpendingAmount) })
                .Where(m => m.Spending > 0)
                .OrderByDescending(m => m.Spending).ThenBy(m => m.Name, StringComparer.Ordinal)
                .Take(TopMerchants)
                .ToList(),
            BiggestPurchase = biggest is null ? null : new ReviewPurchase
            {
                Description = biggest.Description, Amount = biggest.Amount, Date = biggest.Date, Category = Reference(biggest.Category!),
            },
            Months = month is not null ? [] : Enumerable.Range(1, 12)
                .Select(m => new DateTime(year.Value, m, 1, 0, 0, 0, DateTimeKind.Utc))
                .Where(m => m <= today)
                .Select(m => new ReviewMonth
                {
                    Label = m.ToString("MMM", CultureInfo.InvariantCulture),
                    Spending = current.Where(t => t.Date >= m && t.Date < m.AddMonths(1)).Sum(MonthlyRecordsCalculator.SpendingAmount),
                    Income = current.Where(t => t.Date >= m && t.Date < m.AddMonths(1) && t.Flow == Flow.Income).Sum(t => t.Amount),
                })
                .ToList(),
            RecurringCount = recurring.Count,
            RecurringMonthlyTotal = recurring.Sum(p => p.MonthlyCost),
            Earliest = earliest.Items.FirstOrDefault()?.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        };
    }

    private static DateTime RecurringStart(DateTime today) =>
        new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-RecurringLookBackMonths);

    private Task<List<Transaction>> LoadAsync(DateTime start, DateTime end, CancellationToken cancellationToken) =>
        transactionRepository.FilterNoPaginationAsync(new TransactionFilterDto { StartDate = start, EndDate = end }, cancellationToken);

    private async Task<CategoryMatcher> MatcherAsync(CancellationToken cancellationToken) =>
        new(await categoryRuleRepository.GetAllAsync(cancellationToken));

    private async Task<List<DetectedPayment>> DetectRecurringAsync(List<Transaction> transactions, DateTime today, CancellationToken cancellationToken)
    {
        var matcher = await MatcherAsync(cancellationToken);
        return RecurringDetector.Detect(
            transactions.Where(t => t.Flow == Flow.Spending && t.Date <= today)
                .Select(t => new Charge(t.Id, t.Date, t.Amount, MerchantNames.Of(matcher, t.Description), t.Category)),
            today);
    }

    /// <summary>
    /// When a payment is still to come this month: from its next date on, at its cadence. One that is due but in its
    /// grace days from last month is expected any day now; a late one's missed charge isn't counted.
    /// </summary>
    private static IEnumerable<DateTime> Occurrences(RecurringPayment payment, DateTime today, DateTime monthStart, DateTime monthEnd)
    {
        var date = payment.NextDate;
        if (date < today)
        {
            if (payment.Status == RecurringStatus.Active) yield return today;
            date = RecurringDetector.Next(date, payment.Cadence);
        }
        for (; date <= monthEnd; date = RecurringDetector.Next(date, payment.Cadence))
        {
            if (date >= monthStart && date >= today) yield return date;
        }
    }

    /// <summary>Up to <see cref="UsualMonths"/> months just before this one that the data covers from their first day, oldest first.</summary>
    private static List<DateTime> HistoryMonths(List<Transaction> transactions, DateTime monthStart)
    {
        if (transactions.Count == 0) return [];
        var first = transactions.Min(t => t.Date);
        var firstWhole = first.Day == 1
            ? new DateTime(first.Year, first.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(first.Year, first.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return Enumerable.Range(1, UsualMonths)
            .Select(back => monthStart.AddMonths(-back))
            .Where(m => m >= firstWhole)
            .OrderBy(m => m)
            .ToList();
    }

    /// <summary>
    /// Where a monthly or yearly budget should end at its usual pace: what it counted a day over the last complete
    /// months, by today's rules (its categories or merchant, and what is linked to it). Null for the others, and for
    /// one that counts only what is linked: a linked purchase says nothing about the next.
    /// </summary>
    private static BudgetForecast? BudgetForecast(BudgetSummary summary, List<Transaction> transactions, List<DateTime> history,
        int historyDays, DateTime today)
    {
        var budget = summary.Budget;
        if (budget.Period is not (BudgetPeriod.Monthly or BudgetPeriod.Yearly) || budget.Amount <= 0) return null;
        if (summary.Categories.Count == 0 && string.IsNullOrWhiteSpace(budget.Merchant)) return null;

        var categories = summary.Categories.Select(c => c.Id).ToHashSet();
        var merchant = budget.Merchant?.Trim();
        decimal Counted(Transaction t) =>
            t.BudgetId == budget.Id
                ? (t.Flow is Flow.Transfer or Flow.Ignored) ? 0 : t.TransactionType == TransactionType.Credit ? -t.Amount : t.Amount
                : t.BudgetId is null && (t.Flow is Flow.Spending or Flow.Refund)
                  && (categories.Contains(t.CategoryId)
                      || (!string.IsNullOrEmpty(merchant) && t.Description.Contains(merchant, StringComparison.OrdinalIgnoreCase)))
                    ? MonthlyRecordsCalculator.SpendingAmount(t)
                    : 0;

        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rate = historyDays > 0
            ? transactions.Where(t => t.Date >= history[0] && t.Date < monthStart).Sum(Counted) / historyDays
            : summary.TotalSpent / today.Day;
        var end = budget.Period == BudgetPeriod.Monthly
            ? monthStart.AddMonths(1).AddDays(-1)
            : new DateTime(today.Year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var projected = Math.Round(summary.TotalSpent + Math.Max(0, rate) * (end - today).Days, 2);
        return new BudgetForecast
        {
            BudgetId = budget.Id,
            Name = budget.Name,
            Period = budget.Period,
            Amount = budget.Amount,
            Spent = summary.TotalSpent,
            Projected = projected,
            ProjectedPercentage = Math.Round(projected / budget.Amount * 100, 1),
        };
    }

    private static CategoryReferenceDto Reference(Category category) =>
        new() { Id = category.Id, Name = category.Name, Color = category.Color };

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}

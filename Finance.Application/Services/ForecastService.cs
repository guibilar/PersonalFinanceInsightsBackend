using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services;

/// <summary>Where this month's spending is heading: what is spent, the payments still due, and usual spending for the days left.</summary>
public class ForecastService(
    IBudgetRepository budgetRepository,
    RecurringPayments recurringPayments,
    ReportingCalendar calendar) : IForecastService
{
    /// <summary>Complete months that "usual" is over.</summary>
    private const int UsualMonths = 3;

    public async Task<SpendingForecast> GetForecastAsync(CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        var monthStart = calendar.CurrentMonthStart();
        var nextMonth = monthStart.AddMonths(1);
        var monthEnd = nextMonth.AddDays(-1);
        var daysLeft = (monthEnd - today).Days;

        var transactions = await recurringPayments.LoadAsync(RecurringPayments.Start(today), nextMonth, cancellationToken);
        var detected = await recurringPayments.DetectAsync(transactions, today, cancellationToken);
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
}

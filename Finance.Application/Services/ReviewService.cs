using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

/// <summary>A month or a year looked back on, against the one before.</summary>
public class ReviewService(
    ITransactionRepository transactionRepository,
    RecurringPayments recurringPayments,
    ReportingCalendar calendar) : IReviewService
{
    private const int TopMerchants = 5;

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

        var matcher = await recurringPayments.MatcherAsync(cancellationToken);
        var both = await recurringPayments.LoadAsync(previousStart, end, cancellationToken);
        var current = both.Where(t => t.Date >= start).ToList();
        // While the period still runs, the one before counts as far as the same day, so the two compare like for like.
        var complete = today >= end;
        var previousEnd = complete ? start : Min(start, (month is null ? today.AddYears(-1) : today.AddMonths(-1)).AddDays(1));
        var previous = both.Where(t => t.Date < previousEnd).ToList();
        var purchases = current.Where(t => t.Flow == Flow.Spending).ToList();

        var earliest = await transactionRepository.FilterAsync(
            new TransactionFilterDto { SortBy = TransactionSortField.Date, SortDirection = SortDirection.Ascending, Page = 1, PageSize = 1 },
            cancellationToken);
        // The days before the first data say nothing about spending: they aren't days without it.
        var firstDay = earliest.Items.FirstOrDefault()?.Date.Date is { } first && first > start ? first : start;
        var lastDay = earliest.Items.Count == 0 ? start.AddDays(-1) : Min(end.AddDays(-1), today);
        var days = lastDay >= firstDay ? (lastDay - firstDay).Days + 1 : 0;
        var spendDays = purchases.Where(t => t.Date <= lastDay).Select(t => t.Date.Date).Distinct().Count();

        var recurring = (await recurringPayments.DetectAsync(await recurringPayments.LoadAsync(RecurringPayments.Start(today), today.AddDays(1), cancellationToken), today, cancellationToken))
            .Select(d => d.Payment).Where(p => p.Status != RecurringStatus.Stopped).ToList();

        var biggest = purchases.OrderByDescending(t => t.Amount).ThenBy(t => t.Date).FirstOrDefault();
        return new ReviewReport
        {
            Label = Label(start),
            Year = year.Value,
            Month = month,
            Start = start,
            End = end.AddDays(-1),
            Complete = complete,
            PreviousLabel = Label(previousStart),
            PreviousEnd = previousEnd.AddDays(-1),
            Spending = current.Sum(MonthlyRecordsCalculator.SpendingAmount),
            PreviousSpending = previous.Sum(MonthlyRecordsCalculator.SpendingAmount),
            Income = current.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount),
            PreviousIncome = previous.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount),
            Purchases = purchases.Count,
            NoSpendDays = days - spendDays,
            Categories = current.Concat(previous)
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

    private static CategoryReferenceDto Reference(Category category) =>
        new() { Id = category.Id, Name = category.Name, Color = category.Color };

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}

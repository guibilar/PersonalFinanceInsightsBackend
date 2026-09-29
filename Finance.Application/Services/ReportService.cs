using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services;

public class ReportService(ITransactionRepository transactionRepository, ReportingCalendar calendar) : IReportService
{

    public async Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync(CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        var yearStart = new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var transactions = await SpendingAsync(yearStart, yearStart.AddYears(1), cancellationToken);
        return GenerateMonthlyGraphDataFromTransactions(transactions, averageBefore: calendar.CurrentMonthStart());
    }

    public async Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync(CancellationToken cancellationToken = default)
    {
        var monthStart = calendar.CurrentMonthStart();
        var transactions = await SpendingAsync(monthStart, monthStart.AddMonths(1), cancellationToken);
        var report = GenerateMonthlyGraphDataFromTransactions(transactions);

        // Days 1..today of the previous month; on the 30th, February has only 28 of them.
        var previousStart = monthStart.AddMonths(-1);
        var days = Math.Min(calendar.Today().Day, DateTime.DaysInMonth(previousStart.Year, previousStart.Month));
        var previous = await SpendingAsync(previousStart, previousStart.AddDays(days), cancellationToken);
        report.PreviousMonthToDateTotal = previous.Sum(MonthlyRecordsCalculator.SpendingAmount);
        return report;
    }

    public async Task<CashFlowReport> GenerateCashFlowReportAsync(CancellationToken cancellationToken = default)
    {
        var yearStart = new DateTime(calendar.Today().Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var filter = new TransactionFilterDto { StartDate = yearStart, EndDate = yearStart.AddYears(1) };
        var transactions = await transactionRepository.FilterNoPaginationAsync(filter, cancellationToken);

        var months = transactions
            .GroupBy(t => new DateTime(t.Date.Year, t.Date.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var income = g.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount);
                var spending = g.Sum(MonthlyRecordsCalculator.SpendingAmount);
                // What went into savings accounts, from anywhere, minus what came out; ignored rows don't count.
                var saved = g.Where(t => t.Account!.Type == AccountType.Savings && t.Flow != Flow.Ignored)
                    .Sum(t => t.TransactionType == TransactionType.Credit ? t.Amount : -t.Amount);
                return (YearMonth: g.Key, Month: new CashFlowMonth
                {
                    Month = g.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    Income = income,
                    Spending = spending,
                    Net = income - spending,
                    Saved = saved
                });
            })
            .ToList();

        // As for the spending average: the current month isn't over, so it only counts when no month is complete.
        var complete = months.Where(m => m.YearMonth < calendar.CurrentMonthStart()).Select(m => m.Month).ToList();
        var averaged = complete.Count > 0 ? complete : months.Select(m => m.Month).ToList();
        return new CashFlowReport
        {
            Months = months.Select(m => m.Month).ToList(),
            AverageMonthlyIncome = averaged.Count > 0 ? averaged.Average(m => m.Income) : 0,
            AverageMonthlySpending = averaged.Count > 0 ? averaged.Average(m => m.Spending) : 0,
            AverageMonthlySaved = averaged.Count > 0 ? averaged.Average(m => m.Saved) : 0
        };
    }

    /// <summary>Spending and refunds (which reduce spending), on any account; income and transfers stay out.</summary>
    private async Task<List<Transaction>> SpendingAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var filter = new TransactionFilterDto { StartDate = start, EndDate = end };
        var transactions = await transactionRepository.FilterNoPaginationAsync(filter, cancellationToken);
        return transactions.Where(t => t.Flow is Flow.Spending or Flow.Refund).ToList();
    }

    /// <param name="averageBefore">
    /// Only months before this one count towards the average: the current month isn't over, and its partial
    /// total would pull the average down. When there are no earlier months (January), every month counts.
    /// </param>
    private static MonthlyCategorizedGraphData GenerateMonthlyGraphDataFromTransactions(
        List<Transaction> transactions, DateTime? averageBefore = null)
    {
        var monthlyRecords = MonthlyRecordsCalculator.GenerateSpendingRecords(transactions);
        var completeMonths = averageBefore is { } before
            ? monthlyRecords.Where(m => m.YearMonth < before).ToList()
            : [];
        var averaged = completeMonths.Count > 0 ? completeMonths : monthlyRecords;
        // The transactions come with their categories; names are unique, so values are keyed by name.
        var categories = transactions
            .Select(t => t.Category!)
            .DistinctBy(c => c.Id)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = categories.ToDictionary(c => c.Id, c => c.Name);

        var graphData = new MonthlyCategorizedGraphData
        {
            Categories = categories.Select(c => new CategoryReferenceDto { Id = c.Id, Name = c.Name, Color = c.Color }).ToList(),
            // No transactions yet (e.g. the 1st of the month) is a valid, empty report.
            AverageMonthlyTotal = averaged.Count > 0 ? averaged.Average(m => m.Total) : 0,
            MonthlyData = monthlyRecords.Select(m => new MonthDataPoint
            {
                Month = m.YearMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                CategoryValues = m.Categories.ToDictionary(c => names[c.Key], c => c.Value),
                Total = m.Total
            }).ToList()
        };
        return graphData;
    }
}
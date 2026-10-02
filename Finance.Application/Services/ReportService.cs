using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Outputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;
using Finance.Application.Import;

namespace Finance.Application.Services;

public class ReportService(
    ITransactionRepository transactionRepository,
    ICategoryRuleRepository categoryRuleRepository,
    ReportingCalendar calendar) : IReportService
{
    // A few years of weeks; more is a chart nobody can read, and a request that asked for the wrong interval.
    private const int MaxPeriods = 400;
    private const int TopMerchants = 8;

    public async Task<TransactionBreakdown> GenerateBreakdownAsync(TransactionBreakdownInputDto input,
        CancellationToken cancellationToken = default)
    {
        var interval = input.Interval;
        var (start, end) = DateRange(input);
        var transactions = await MatchingAsync(input, start, end, cancellationToken);
        var merchants = await MerchantsAsync(transactions, cancellationToken);

        var breakdown = new TransactionBreakdown
        {
            Interval = interval,
            Count = transactions.Count,
            Spending = transactions.Sum(MonthlyRecordsCalculator.SpendingAmount),
            Refunds = transactions.Where(t => t.Flow == Flow.Refund).Sum(t => t.Amount),
            Income = transactions.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount),
            TopMerchants = merchants.Take(TopMerchants).ToList(),
            MerchantCount = merchants.Count,
        };

        // From the first period asked for (or with a transaction) to the last asked for, or up to today: months with
        // nothing in them count too, for the average and the running total.
        var today = calendar.Today();
        DateTime? first = start ?? (transactions.Count > 0 ? transactions.Min(t => t.Date) : null);
        DateTime? last = end ?? (transactions.Count > 0 ? Max(transactions.Max(t => t.Date), today) : start is null ? null : Max(start.Value, today));
        if (first is null || last is null) return breakdown;

        // Too many periods to chart (years of weeks): the next longer interval, as the totals above don't depend on
        // it and failing would lose them too. The breakdown says which interval it used.
        var count = BreakdownPeriods.Count(first.Value, last.Value, interval);
        while (count > MaxPeriods && interval < BreakdownInterval.Year)
        {
            interval++;
            count = BreakdownPeriods.Count(first.Value, last.Value, interval);
        }
        if (count > MaxPeriods)
        {
            throw new InvalidInputException("startDate",
                $"That is {count} years; choose a shorter period.");
        }
        breakdown.Interval = interval;

        var byPeriod = transactions.ToLookup(t => BreakdownPeriods.StartOf(t.Date, interval));
        decimal spending = 0, income = 0;
        for (var periodStart = BreakdownPeriods.StartOf(first.Value, interval); periodStart <= last.Value;
             periodStart = BreakdownPeriods.Next(periodStart, interval))
        {
            var inPeriod = byPeriod[periodStart].ToList();
            var period = new BreakdownPeriod
            {
                Start = periodStart,
                End = BreakdownPeriods.Next(periodStart, interval).AddDays(-1),
                Label = BreakdownPeriods.Label(periodStart, interval),
                Count = inPeriod.Count,
                Spending = inPeriod.Sum(MonthlyRecordsCalculator.SpendingAmount),
                Income = inPeriod.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount),
            };
            period.CumulativeSpending = spending += period.Spending;
            period.CumulativeIncome = income += period.Income;
            breakdown.Periods.Add(period);
        }

        // As on the dashboard: the running period isn't over, so it only counts when no period is.
        var over = breakdown.Periods.Where(p => p.End < today).ToList();
        var averaged = over.Count > 0 ? over : breakdown.Periods;
        breakdown.AveragedPeriods = averaged.Count;
        breakdown.AverageSpending = averaged.Average(p => p.Spending);
        breakdown.AverageIncome = averaged.Average(p => p.Income);
        return breakdown;
    }

    public async Task<PaginatedList<MerchantTotal>> GetMerchantsAsync(MerchantsInputDto input,
        CancellationToken cancellationToken = default)
    {
        var (start, end) = DateRange(input);
        var merchants = await MerchantsAsync(await MatchingAsync(input, start, end, cancellationToken), cancellationToken);
        return new PaginatedList<MerchantTotal>
        {
            Items = merchants.Skip((input.Page - 1) * input.PageSize).Take(input.PageSize).ToList(),
            Page = input.Page,
            PageSize = input.PageSize,
            TotalCount = merchants.Count,
        };
    }

    /// <summary>The criteria's days, as UTC dates; an end date too far out to add a day to is no end.</summary>
    private static (DateTime? Start, DateTime? End) DateRange(TransactionCriteriaInputDto input)
    {
        var start = input.StartDate is { } from ? DateTime.SpecifyKind(from.Date, DateTimeKind.Utc) : (DateTime?)null;
        var end = input.EndDate is { } to && to.Date < DateTime.MaxValue.Date
            ? DateTime.SpecifyKind(to.Date, DateTimeKind.Utc)
            : (DateTime?)null;
        if (start > end)
        {
            throw new InvalidInputException("endDate", "The end date is before the start date.");
        }

        return (start, end);
    }

    /// <summary>The list's criteria, so the totals are the list's; its end date is inclusive, the repository's isn't.</summary>
    private Task<List<Transaction>> MatchingAsync(TransactionCriteriaInputDto input, DateTime? start, DateTime? end,
        CancellationToken cancellationToken) =>
        transactionRepository.FilterNoPaginationAsync(new TransactionFilterDto
        {
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            CategoryId = input.CategoryId,
            AccountId = input.AccountId,
            Flow = input.Flow,
            StartDate = start,
            EndDate = end?.AddDays(1),
        }, cancellationToken);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>
    /// Spending grouped by merchant: the category rules name merchants, so the keyword of the rule that decides a
    /// description ("NO FRILLS" for every store of the chain); the description itself when no rule matches.
    /// </summary>
    private async Task<List<MerchantTotal>> MerchantsAsync(List<Transaction> transactions, CancellationToken cancellationToken)
    {
        var matcher = new CategoryMatcher(await categoryRuleRepository.GetAllAsync(cancellationToken));
        return transactions
            .Where(t => t.Flow is Flow.Spending or Flow.Refund)
            .GroupBy(t => matcher.MatchingKeyword(t.Description) ?? string.Join(' ', t.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Select(g => new MerchantTotal { Name = g.Key, Count = g.Count(), Spending = g.Sum(MonthlyRecordsCalculator.SpendingAmount) })
            .Where(m => m.Spending > 0)
            .OrderByDescending(m => m.Spending)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

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

        // A month is listed when something in it counts: a month of nothing but transfers and ignored rows would
        // only be a row of zeros that pulls the averages down.
        var months = transactions
            .Where(CountsInCashFlow)
            .GroupBy(t => new DateTime(t.Date.Year, t.Date.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var income = g.Where(t => t.Flow == Flow.Income).Sum(t => t.Amount);
                var spending = g.Sum(MonthlyRecordsCalculator.SpendingAmount);
                // What went into savings accounts, from anywhere, minus what came out.
                var savingsChange = g.Where(t => t.Account!.Type == AccountType.Savings)
                    .Sum(t => t.TransactionType == TransactionType.Credit ? t.Amount : -t.Amount);
                return (YearMonth: g.Key, Month: new CashFlowMonth
                {
                    Month = g.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                    Income = income,
                    Spending = spending,
                    Net = income - spending,
                    SavingsChange = savingsChange
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
            AverageMonthlyNet = averaged.Count > 0 ? averaged.Average(m => m.Net) : 0,
            AverageMonthlySavingsChange = averaged.Count > 0 ? averaged.Average(m => m.SavingsChange) : 0,
            AveragedMonths = averaged.Count,
            AveragesIncludeCurrentMonth = complete.Count == 0 && averaged.Count > 0
        };
    }

    /// <summary>Whether a transaction changes any of the cash-flow figures: income, spending, or a savings account.</summary>
    private static bool CountsInCashFlow(Transaction transaction) =>
        transaction.Flow is Flow.Spending or Flow.Refund or Flow.Income
        || (transaction.Account!.Type == AccountType.Savings && transaction.Flow != Flow.Ignored);

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
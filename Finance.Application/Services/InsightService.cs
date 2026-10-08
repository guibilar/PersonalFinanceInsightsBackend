using Finance.Application.Abstractions;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services;

/// <summary>What the spending says on its own: the payments that recur, and what no rule sorts yet.</summary>
public class InsightService(
    ITransactionRepository transactionRepository,
    RecurringPayments recurringPayments,
    ReportingCalendar calendar) : IInsightService
{
    private const int MaxUncategorised = 50;

    public async Task<RecurringReport> GetRecurringAsync(CancellationToken cancellationToken = default)
    {
        var today = calendar.Today();
        var detected = await recurringPayments.DetectAsync(await recurringPayments.LoadAsync(RecurringPayments.Start(today), today.AddDays(1), cancellationToken), today, cancellationToken);
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

    public async Task<UncategorisedReport> GetUncategorisedAsync(CancellationToken cancellationToken = default)
    {
        var matcher = await recurringPayments.MatcherAsync(cancellationToken);
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
}

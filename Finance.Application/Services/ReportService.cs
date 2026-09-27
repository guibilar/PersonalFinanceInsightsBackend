using System.Globalization;
using Finance.Application.Abstractions;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
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
        return GenerateMonthlyGraphDataFromTransactions(transactions);
    }

    public async Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync(CancellationToken cancellationToken = default)
    {
        var monthStart = calendar.CurrentMonthStart();
        var transactions = await SpendingAsync(monthStart, monthStart.AddMonths(1), cancellationToken);
        return GenerateMonthlyGraphDataFromTransactions(transactions);
    }

    /// <summary>
    /// Purchases from chequing and the credit card, plus credit card refunds (which reduce spending).
    /// Money in on chequing is income, not a refund, so it stays out.
    /// </summary>
    private async Task<List<Transaction>> SpendingAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var filter = new TransactionFilterDto
        {
            AccountType = [AccountType.Chequing, AccountType.CreditCard],
            StartDate = start,
            EndDate = end
        };
        var transactions = await transactionRepository.FilterNoPaginationAsync(filter, cancellationToken);
        return transactions
            .Where(t => t.TransactionType == TransactionType.Debit ||
                        (t.TransactionType == TransactionType.Credit && t.Account == AccountType.CreditCard))
            .ToList();
    }

    private static MonthlyCategorizedGraphData GenerateMonthlyGraphDataFromTransactions(List<Transaction> transactions)
    {
        var monthlyRecords = MonthlyRecordsCalculator.GenerateSpendingRecords(transactions);
        var categories = transactions
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var graphData = new MonthlyCategorizedGraphData
        {
            Categories = categories,
            // No transactions yet (e.g. the 1st of the month) is a valid, empty report.
            AverageMonthlyTotal = monthlyRecords.Count > 0 ? monthlyRecords.Average(m => m.Total) : 0,
            MonthlyData = monthlyRecords.Select(m => new MonthDataPoint
            {
                Month = m.YearMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                CategoryValues = m.Categories,
                Total = m.Total
            }).ToList()
        };
        return graphData;
    }
}
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
        var filterForMonthlyCategorizedGraphy = new TransactionFilterDto()
        {
            AccountType = [
                AccountType.Chequing,
                AccountType.CreditCard
            ],
            TransactionType = TransactionType.Debit,
            StartDate = yearStart,
            EndDate = yearStart.AddYears(1)
        };
        var transactions = await transactionRepository.FilterNoPaginationAsync(filterForMonthlyCategorizedGraphy, cancellationToken);
        var graphData = GenerateMonthlyGraphDataFromTransactions(transactions);
        return graphData;
    }
    
    public async Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync(CancellationToken cancellationToken = default)
    {
        var monthStart = calendar.CurrentMonthStart();
        var filterForCurrentMonthCategorizedGraphy = new TransactionFilterDto()
        {
            AccountType = [
                AccountType.Chequing,
                AccountType.CreditCard
            ],
            TransactionType = TransactionType.Debit,
            StartDate = monthStart,
            EndDate = monthStart.AddMonths(1)
        };
        var transactions = await transactionRepository.FilterNoPaginationAsync(filterForCurrentMonthCategorizedGraphy, cancellationToken);
        var graphData = GenerateMonthlyGraphDataFromTransactions(transactions);
        return graphData;
    }

    private static MonthlyCategorizedGraphData GenerateMonthlyGraphDataFromTransactions(List<Transaction> transactions)
    {
        var monthlyRecords = MonthlyRecordsCalculator.GenerateMonthlyRecords(transactions);
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
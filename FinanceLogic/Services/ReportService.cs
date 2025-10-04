using CrossCutting.Calculators;
using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Enums;
using FinanceDto.Inputs;
using FinanceDto.Models;

namespace CrossCutting.Services;

public class ReportService(ITransactionRepository transactionRepository) : IReportService
{
    public async Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync()
    {
        var filterForMonthlyCategorizedGraphy = new TransactionFilterDto()
        {
            AccountType = [
                AccountType.Chequing,
                AccountType.CreditCard
            ],
            TransactionType = TransactionType.Debit,
            StartDate = new DateTime(DateTime.Now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(DateTime.Now.Year, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };
        var transactions = await transactionRepository.FilterAsync(filterForMonthlyCategorizedGraphy);
        var graphData = GenerateMonthlyGraphDataFromTransactions(transactions);
        return graphData;
    }
    
    public async Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync()
    {
        var filterForCurrentMonthCategorizedGraphy = new TransactionFilterDto()
        {
            AccountType = [
                AccountType.Chequing,
                AccountType.CreditCard
            ],
            TransactionType = TransactionType.Debit,
            StartDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month), 23, 59, 59, DateTimeKind.Utc)
        };
        var transactions = await transactionRepository.FilterAsync(filterForCurrentMonthCategorizedGraphy);
        var graphData = GenerateMonthlyGraphDataFromTransactions(transactions);
        return graphData;
    }

    private static MonthlyCategorizedGraphData GenerateMonthlyGraphDataFromTransactions(List<Transaction> transactions)
    {
        var monthlyRecords = SpendingCalculator.GenerateMonthlyRecords(transactions);
        var categories = transactions
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var graphData = new MonthlyCategorizedGraphData
        {
            Categories = categories,
            AverageMonthlyTotal = monthlyRecords.Average(m => m.Total),
            MonthlyData = monthlyRecords.Select(m => new MonthDataPoint
            {
                Month = m.YearMonth.ToString("MMM yyyy"),
                CategoryValues = m.Categories,
                Total = m.Total
            }).ToList()
        };
        return graphData;
    }
}
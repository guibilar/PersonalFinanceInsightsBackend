using CrossCutting.Calculators;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Models;

namespace CrossCutting.Services;

public class ReportService(ITransactionRepository transactionRepository) : IReportService
{
    public MonthlyCategorizedGraphData GenerateMonthlyCategorizedGraphData()
    {
        var transactions = transactionRepository.GetForCategoryReport();
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

public interface IReportService
{
    MonthlyCategorizedGraphData GenerateMonthlyCategorizedGraphData();
}
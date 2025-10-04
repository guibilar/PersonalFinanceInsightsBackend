using FinanceDto.Models;

namespace CrossCutting.Services;

public interface IReportService
{
    Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync();
    Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync();
}
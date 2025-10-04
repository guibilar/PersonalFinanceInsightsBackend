using FinanceDto.Models;

namespace CrossCutting.Services.Interfaces;

public interface IReportService
{
    Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync();
    Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync();
}


using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface IReportService
{
    Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync();
    Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync();
}
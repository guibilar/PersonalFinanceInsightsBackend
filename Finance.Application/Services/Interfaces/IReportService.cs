

using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface IReportService
{
    Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync(CancellationToken cancellationToken = default);
    Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Income, spending and savings per month of the current year.</summary>
    Task<CashFlowReport> GenerateCashFlowReportAsync(CancellationToken cancellationToken = default);
}
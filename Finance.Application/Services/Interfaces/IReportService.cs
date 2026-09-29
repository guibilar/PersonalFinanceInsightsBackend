

using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface IReportService
{
    Task<MonthlyCategorizedGraphData> GenerateMonthlySpendCategorizedGraphDataAsync(CancellationToken cancellationToken = default);
    Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraphDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Totals for the transactions the list's criteria pick, and their spending and income per week, month, quarter
    /// or year, with running totals and where the spending went.
    /// </summary>
    Task<TransactionBreakdown> GenerateBreakdownAsync(TransactionBreakdownInputDto input, CancellationToken cancellationToken = default);

    /// <summary>Income, spending and savings per month of the current year.</summary>
    Task<CashFlowReport> GenerateCashFlowReportAsync(CancellationToken cancellationToken = default);
}
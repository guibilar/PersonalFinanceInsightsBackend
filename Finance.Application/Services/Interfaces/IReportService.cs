

using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;

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

    /// <summary>A page of the merchants the list's criteria spent at, largest first, as the breakdown ranks them.</summary>
    Task<PaginatedList<MerchantTotal>> GetMerchantsAsync(MerchantsInputDto input, CancellationToken cancellationToken = default);

    /// <summary>Income, spending and savings per month of the current year.</summary>
    Task<CashFlowReport> GenerateCashFlowReportAsync(CancellationToken cancellationToken = default);
}
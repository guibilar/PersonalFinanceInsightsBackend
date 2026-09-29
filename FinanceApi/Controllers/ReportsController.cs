using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class ReportsController(IReportService reportService) : ControllerBase
{
    [HttpGet("monthly-categorized-graph")]
    public Task<MonthlyCategorizedGraphData> GenerateMonthlyCategorizedGraph(CancellationToken cancellationToken)
    {
        return reportService.GenerateMonthlySpendCategorizedGraphDataAsync(cancellationToken);
    }

    [HttpGet("current-month-categorized-graph")]
    public Task<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraph(CancellationToken cancellationToken)
    {
        return reportService.GenerateCurrentMonthCategorizedGraphDataAsync(cancellationToken);
    }

    /// <summary>
    /// Totals for the transactions the list's criteria pick, and how they spread over weeks, months, quarters or years
    /// (running totals included), with the merchants the spending went to.
    /// </summary>
    [HttpPost("breakdown")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<TransactionBreakdown> GenerateBreakdown(TransactionBreakdownInputDto input, CancellationToken cancellationToken)
    {
        return reportService.GenerateBreakdownAsync(input, cancellationToken);
    }

    /// <summary>Income, spending and money saved per month of the current year.</summary>
    [HttpGet("cash-flow")]
    public Task<CashFlowReport> GenerateCashFlowReport(CancellationToken cancellationToken)
    {
        return reportService.GenerateCashFlowReportAsync(cancellationToken);
    }
}

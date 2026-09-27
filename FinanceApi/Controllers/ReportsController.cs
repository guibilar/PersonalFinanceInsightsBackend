using Finance.Application.Services.Interfaces;
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
}

using CrossCutting.Services;
using FinanceDto.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController(IReportService reportService) : ControllerBase
    {
        [HttpGet("monthly-categorized-graph")]
        public async Task<ActionResult<MonthlyCategorizedGraphData>> GenerateMonthlyCategorizedGraph()
        {
            var graphData = await reportService.GenerateMonthlySpendCategorizedGraphDataAsync();
            return Ok(graphData);
        }
        
        [HttpGet("current-month-categorized-graph")]
        public async Task<ActionResult<MonthlyCategorizedGraphData>> GenerateCurrentMonthCategorizedGraph()
        {
            var graphData = await reportService.GenerateCurrentMonthCategorizedGraphDataAsync();
            return Ok(graphData);
        }
    }
}
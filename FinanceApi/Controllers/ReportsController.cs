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
        public ActionResult<MonthlyCategorizedGraphData> GenerateMonthlyCategorizedGraph()
        {
            var graphData = reportService.GenerateMonthlySpendCategorizedGraphData();
            return Ok(graphData);
        }
        
        [HttpGet("current-month-categorized-graph")]
        public ActionResult<MonthlyCategorizedGraphData> GenerateCurrentMonthCategorizedGraph()
        {
            var graphData = reportService.GenerateCurrentMonthCategorizedGraphData();
            return Ok(graphData);
        }
    }
}
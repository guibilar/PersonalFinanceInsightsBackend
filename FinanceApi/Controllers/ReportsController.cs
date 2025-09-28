using CrossCutting.Services;
using FinanceDomain.Models;
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
            var graphData = reportService.GenerateMonthlyCategorizedGraphData();
            return Ok(graphData);
        }
    }
}
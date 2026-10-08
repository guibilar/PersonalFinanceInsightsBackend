using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Contracts.Reports;
using Finance.Domain.Common;
using Finance.Domain.Exceptions;
using FinanceApi.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApi.Controllers;

[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class ReportsController(
    IReportService reportService,
    IInsightService insightService,
    IForecastService forecastService,
    IReviewService reviewService,
    INetWorthService netWorthService) : ControllerBase
{
    /// <summary>Payments that come again and again (subscriptions, bills, rent): what they cost, when the next is due, price changes.</summary>
    [HttpGet("recurring")]
    public Task<RecurringReport> GetRecurring(CancellationToken cancellationToken)
    {
        return insightService.GetRecurringAsync(cancellationToken);
    }

    /// <summary>Where this month's spending is heading, the recurring payments still due, and each monthly and yearly budget.</summary>
    [HttpGet("forecast")]
    public Task<SpendingForecast> GetForecast(CancellationToken cancellationToken)
    {
        return forecastService.GetForecastAsync(cancellationToken);
    }

    /// <summary>Merchants whose spending is left in Other because no rule names them, each with a keyword for a rule.</summary>
    [HttpGet("uncategorised")]
    public Task<UncategorisedReport> GetUncategorised(CancellationToken cancellationToken)
    {
        return insightService.GetUncategorisedAsync(cancellationToken);
    }

    /// <summary>
    /// A month (<paramref name="year"/> and <paramref name="month"/>) or a year (<paramref name="year"/> alone) looked
    /// back on, against the one before; the last complete month without either.
    /// </summary>
    [HttpGet("review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<ReviewReport> GetReview([FromQuery] int? year, [FromQuery] int? month, CancellationToken cancellationToken)
    {
        if (year is null && month is not null) throw new InvalidInputException("year", "Choose the month's year too.");
        return reviewService.GetReviewAsync(year, month, cancellationToken);
    }

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
    [ReadOnlyQuery]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<TransactionBreakdown> GenerateBreakdown(TransactionBreakdownInputDto input, CancellationToken cancellationToken)
    {
        return reportService.GenerateBreakdownAsync(input, cancellationToken);
    }

    /// <summary>
    /// A page of the merchants the list's criteria spent at, largest first: the breakdown's top ones, and the rest.
    /// </summary>
    [HttpPost("merchants")]
    [ReadOnlyQuery]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<PaginatedList<MerchantTotal>> GetMerchants(MerchantsInputDto input, CancellationToken cancellationToken)
    {
        return reportService.GetMerchantsAsync(input, cancellationToken);
    }

    /// <summary>Income, spending, what was left over and how the savings accounts changed, per month of the current year.</summary>
    [HttpGet("cash-flow")]
    public Task<CashFlowReport> GenerateCashFlowReport(CancellationToken cancellationToken)
    {
        return reportService.GenerateCashFlowReportAsync(cancellationToken);
    }

    /// <summary>What every account holds or owes today and at the end of each of the last twelve months, and the total.</summary>
    [HttpGet("net-worth")]
    public Task<NetWorthReport> GetNetWorth(CancellationToken cancellationToken)
    {
        return netWorthService.GetAsync(cancellationToken);
    }
}

using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface IInsightService
{
    /// <summary>Payments that come again and again (subscriptions, bills, rent), found in the last two years of spending.</summary>
    Task<RecurringReport> GetRecurringAsync(CancellationToken cancellationToken = default);

    /// <summary>The merchants whose spending is left in Other because no rule names them, most transactions first.</summary>
    Task<UncategorisedReport> GetUncategorisedAsync(CancellationToken cancellationToken = default);
}

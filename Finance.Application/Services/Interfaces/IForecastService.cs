using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface IForecastService
{
    /// <summary>Where this month's spending is heading, and each monthly and yearly budget with it.</summary>
    Task<SpendingForecast> GetForecastAsync(CancellationToken cancellationToken = default);
}

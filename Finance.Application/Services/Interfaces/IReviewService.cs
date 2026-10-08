using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface IReviewService
{
    /// <summary>A month (or, without <paramref name="month"/>, a year) looked back on; the last complete month without either.</summary>
    Task<ReviewReport> GetReviewAsync(int? year, int? month, CancellationToken cancellationToken = default);
}

using Finance.Contracts.Reports;

namespace Finance.Application.Services.Interfaces;

public interface INetWorthService
{
    Task<NetWorthReport> GetAsync(CancellationToken cancellationToken = default);
}

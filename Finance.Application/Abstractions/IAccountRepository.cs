using Finance.Application.Models;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IAccountRepository : IBaseRepository<Account>
{
    /// <summary>Tracked, by kind then name.</summary>
    Task<List<Account>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>By kind then name, with how many transactions each has (deleted ones included).</summary>
    Task<List<AccountSummary>> GetSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>Deleted ones included: they are kept so re-importing doesn't bring them back.</summary>
    Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default);
}

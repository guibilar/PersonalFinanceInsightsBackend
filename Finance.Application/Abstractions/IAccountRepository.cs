using Finance.Application.Models;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IAccountRepository : IBaseRepository<Account>
{
    /// <summary>Tracked, by kind then name.</summary>
    Task<List<Account>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// By kind then name, with how many transactions each has (deleted ones included), its latest balance, and the
    /// transfers in and out of it during <paramref name="year"/>.
    /// </summary>
    Task<List<AccountSummary>> GetSummariesAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>The account's summary, as in <see cref="GetSummariesAsync"/>.</summary>
    Task<AccountSummary> GetSummaryAsync(Account account, int year, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<List<AccountBalance>> GetBalancesAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>Every account's balances, oldest first.</summary>
    Task<List<AccountBalance>> GetAllBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked.</summary>
    Task<AccountBalance?> GetBalanceAsync(Guid accountId, DateTime date, CancellationToken cancellationToken = default);

    Task<AccountBalance?> GetBalanceByIdAsync(Guid accountId, Guid balanceId, CancellationToken cancellationToken = default);
    Task AddBalanceAsync(AccountBalance balance, CancellationToken cancellationToken = default);
    Task RemoveBalanceAsync(AccountBalance balance, CancellationToken cancellationToken = default);

    /// <summary>Deleted ones included: they are kept so re-importing doesn't bring them back.</summary>
    Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default);
}

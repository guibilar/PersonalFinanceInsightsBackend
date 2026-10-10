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

    Task<AccountBalance?> GetBalanceByIdAsync(Guid accountId, Guid balanceId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Sets the account's balance on <paramref name="date"/>, adding it or changing the one there: one stored at the
    /// same moment by another request is changed rather than refused.
    /// </summary>
    Task<AccountBalance> SetBalanceAsync(Guid accountId, DateTime date, decimal amount, CancellationToken cancellationToken = default);
    Task RemoveBalanceAsync(AccountBalance balance, CancellationToken cancellationToken = default);

    /// <summary>Deleted ones included: they are kept so re-importing doesn't bring them back.</summary>
    Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default);
}

using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services.Interfaces;

/// <summary>
/// The accounts statements are imported into. Names are unique ignoring case; the kind never changes. Savings and
/// investment accounts can be held in a registered plan, with its contribution room; any account can have balances.
/// </summary>
public interface IAccountService
{
    /// <summary>By kind, then name.</summary>
    Task<List<AccountSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AccountSummary> AddAsync(AccountType type, AccountDetails details, CancellationToken cancellationToken = default);
    Task<AccountSummary> UpdateAsync(Guid id, AccountDetails details, CancellationToken cancellationToken = default);

    /// <summary>Only an account without transactions (deleted ones included) can be deleted.</summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<List<AccountBalance>> GetBalancesAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>The balance at the end of <paramref name="date"/>, replacing one entered for that day before.</summary>
    Task<AccountBalance> SetBalanceAsync(Guid accountId, DateOnly date, decimal amount, CancellationToken cancellationToken = default);

    Task RemoveBalanceAsync(Guid accountId, Guid balanceId, CancellationToken cancellationToken = default);
}

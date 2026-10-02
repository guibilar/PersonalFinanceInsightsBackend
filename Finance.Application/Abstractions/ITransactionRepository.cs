using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface ITransactionRepository : IBaseRepository<Transaction>
{
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    /// <summary>Tracked: every transaction whose category the rules decide (not hand-picked, not on a savings account).</summary>
    Task<List<Transaction>> GetRuleCategorizedAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked, with their account: every transaction whose category or flow the rules decide, deleted ones included.</summary>
    Task<List<Transaction>> GetRuleManagedAsync(CancellationToken cancellationToken = default);

    /// <summary>Tracked, hidden or not.</summary>
    Task<Transaction?> GetIncludingHiddenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hides the rows generated as <see cref="Import.LegacyMonthlyRent"/> in these months (their first day) and returns
    /// how many were hidden.
    /// </summary>
    Task<int> HideLegacyMonthlyRentAsync(IReadOnlyCollection<DateTime> months, CancellationToken cancellationToken = default);

    /// <summary>The stored transaction with this hash on the account, hidden or not; null when there is none.</summary>
    Task<Transaction?> FindByHashAsync(Guid accountId, string hash, CancellationToken cancellationToken = default);

    /// <summary>How many of these hashes each account already has stored (hidden ones included); accounts with none are left out.</summary>
    Task<Dictionary<Guid, int>> CountHashesByAccountAsync(IReadOnlyCollection<string> hashes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the transactions whose hash is not yet stored for the account, all or nothing, and returns how many
    /// were added. Concurrent calls for the same account run one after the other.
    /// </summary>
    Task<int> AddMissingAsync(Guid accountId, IReadOnlyCollection<Transaction> transactions, CancellationToken cancellationToken = default);
}

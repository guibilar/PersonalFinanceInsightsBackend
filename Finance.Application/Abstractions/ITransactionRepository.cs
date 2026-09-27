using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Abstractions;

public interface ITransactionRepository : IBaseRepository<Transaction>
{
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    /// <summary>Tracked: every transaction whose category the rules decide (not hand-picked, not savings).</summary>
    Task<List<Transaction>> GetRuleCategorizedAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsByHashAsync(AccountType account, string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the transactions whose hash is not yet stored for the account, all or nothing, and returns how many
    /// were added. Concurrent calls for the same account run one after the other.
    /// </summary>
    Task<int> AddMissingAsync(AccountType account, IReadOnlyCollection<Transaction> transactions, CancellationToken cancellationToken = default);
}

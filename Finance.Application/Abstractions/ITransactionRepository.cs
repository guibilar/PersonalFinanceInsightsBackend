using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface ITransactionRepository : IBaseRepository<Transaction>
{
    Task<List<Transaction>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default);
}

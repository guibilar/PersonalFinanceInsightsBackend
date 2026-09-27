

using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface ITransactionRepository : IBaseRepository<Transaction>
{
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto? filter);
    Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
}
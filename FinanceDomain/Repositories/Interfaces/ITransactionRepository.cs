using FinanceDomain.Models;
using FinanceDto.Inputs;
using FinanceDto.Models;
using FinanceDto.Outputs;

namespace FinanceDomain.Repositories.Interfaces;

public interface ITransactionRepository : IBaseRepository<Transaction>
{
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto? filter);
    Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
}
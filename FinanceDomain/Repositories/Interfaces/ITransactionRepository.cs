using FinanceDomain.Models;
using FinanceDto.Inputs;

namespace FinanceDomain.Repositories.Interfaces;

public interface ITransactionRepository : IBaseRepository<Transaction>
{
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
}
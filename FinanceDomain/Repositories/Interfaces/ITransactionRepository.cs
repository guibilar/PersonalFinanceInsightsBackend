using FinanceDomain.Models;
using FinanceDto.Inputs;

namespace FinanceDomain.Repositories.Interfaces;

public interface ITransactionRepository
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task<List<Transaction>> GetAllAsync();
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
    Task RemoveAsync(Guid id);
}
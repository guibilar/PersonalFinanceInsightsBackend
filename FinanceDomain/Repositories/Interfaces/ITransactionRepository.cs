using FinanceDomain.Models;
using FinanceDto.Inputs;

namespace FinanceDomain.Repositories.Interfaces;

public interface ITransactionRepository
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task AddListAsync(List<Transaction> transactions);
    Task<List<Transaction>> GetAllAsync();
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
}
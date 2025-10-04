using FinanceDomain.Models;
using FinanceDto.Inputs;

namespace CrossCutting.Services.Interfaces;

public interface ITransactionService
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task SyncTransactionsByFileAsync();
    Task<List<Transaction>> GetAllAsync();
    Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
    Task RemoveAsync(Guid id);
}
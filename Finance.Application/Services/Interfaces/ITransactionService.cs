

using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Services.Interfaces;

public interface ITransactionService
{
    Task<Transaction> AddAsync(Transaction transaction);
    Task SyncTransactionsByFileAsync();
    Task<List<Transaction>> GetAllAsync();
    Task<PaginatedList<Transaction>>FilterAsync(TransactionFilterDto? filter);
    Task<Transaction?> GetByHashAsync(string hash);
    Task RemoveAsync(Guid id);
    Task LinkTransactionToBudget(Guid budgetId, Guid transactionId);
    Task UnlinkTransactionFromBudget(Guid transactionId);
}
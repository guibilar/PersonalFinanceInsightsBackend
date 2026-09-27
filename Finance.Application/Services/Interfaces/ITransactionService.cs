using Finance.Application.Import;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services.Interfaces;

public interface ITransactionService
{
    Task<Transaction> AddAsync(Transaction transaction, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportAsync(Stream statement, AccountType account, CancellationToken cancellationToken = default);
    Task<List<Transaction>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
    Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default);
    Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default);
}

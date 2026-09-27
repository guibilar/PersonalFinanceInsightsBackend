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
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    /// <summary>Hides the transaction: gone from lists and reports, but a re-import doesn't add it back.</summary>
    Task HideAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Brings back a hidden transaction, with its category and budget link.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Corrects the description and category; a changed category is then kept when rules are re-applied.</summary>
    Task<Transaction> UpdateAsync(Guid id, string description, Category category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the current category rules over stored transactions, except hand-picked categories, savings and the
    /// fixed monthly rent.
    /// </summary>
    Task<CategoryRulesResult> ApplyCategoryRulesAsync(CancellationToken cancellationToken = default);
    Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default);
    Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default);
}

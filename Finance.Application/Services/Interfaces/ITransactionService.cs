using Finance.Application.Import;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services.Interfaces;

public interface ITransactionService
{
    /// <summary>
    /// Adds a transaction entered by hand. The rules decide the category and flow that weren't picked (locked).
    /// An identical transaction is a conflict unless <paramref name="allowDuplicate"/>.
    /// </summary>
    Task<Transaction> AddAsync(Transaction transaction, bool allowDuplicate = false, CancellationToken cancellationToken = default);
    /// <summary>
    /// Imports the statement into the account, all rows or none. Unless <paramref name="confirmed"/>, a statement that
    /// doesn't look like the account's is refused with <see cref="Domain.Exceptions.AccountMismatchException"/>.
    /// </summary>
    Task<ImportResult> ImportAsync(Stream statement, Guid accountId, bool confirmed, CancellationToken cancellationToken = default);
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    /// <summary>Hides the transaction: gone from lists and reports, but a re-import doesn't add it back.</summary>
    Task HideAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Brings back a hidden transaction, with its category and budget link.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the description, category and (when given) flow; a changed category or flow is then kept when the
    /// rules are re-applied.
    /// </summary>
    Task<Transaction> UpdateAsync(Guid id, string description, Category category, Flow? flow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the current category and flow rules over stored transactions, except hand-picked (locked) categories
    /// and flows, and the categories of savings accounts' transactions.
    /// </summary>
    Task<RulesResult> ApplyRulesAsync(CancellationToken cancellationToken = default);
    Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default);
    Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default);
}

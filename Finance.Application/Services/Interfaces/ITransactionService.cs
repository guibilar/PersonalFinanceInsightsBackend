using Finance.Application.Import;
using Finance.Application.Models;
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
    Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default);
    /// <summary>Hides the transaction: gone from lists and reports, but a re-import doesn't add it back.</summary>
    Task HideAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Brings back a hidden transaction, with its category and budget link.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the description, category and (when given) flow; a changed category or flow is then kept when the
    /// rules are re-applied.
    /// </summary>
    /// <summary>[note] and [tagIds]: unchanged when null; an empty note clears it, no ids remove every tag.</summary>
    Task<Transaction> UpdateAsync(Guid id, string description, Guid categoryId, Flow? flow, string? note = null,
        IReadOnlyCollection<Guid>? tagIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the current category and flow rules over stored transactions, except hand-picked (locked) categories
    /// and flows, and the categories of savings accounts' transactions.
    /// </summary>
    Task<RulesResult> ApplyRulesAsync(CancellationToken cancellationToken = default);
    Task LinkTransactionToBudgetAsync(Guid budgetId, Guid transactionId, CancellationToken cancellationToken = default);
    Task<BulkUpdateResult> BulkUpdateAsync(BulkUpdate update, CancellationToken cancellationToken = default);
    Task UnlinkTransactionFromBudgetAsync(Guid transactionId, CancellationToken cancellationToken = default);
}

using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IBudgetRepository : IBaseRepository<Budget>
{
    /// <summary>Tracked, with its linked transactions (only deleting a budget needs them).</summary>
    Task<Budget?> GetWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The budgets that count these categories by themselves (other than <paramref name="exceptBudgetId"/>): budget
    /// name by category name.
    /// </summary>
    Task<Dictionary<string, string>> GetCategoryOwnersAsync(IReadOnlyCollection<Guid> categoryIds, Guid exceptBudgetId, CancellationToken cancellationToken = default);

    /// <param name="today">Which month and year are current: monthly and yearly budgets only count their dates.</param>
    Task<BudgetSummary?> GetSummaryAsync(Guid id, DateTime today, CancellationToken cancellationToken = default);

    /// <summary>Every active budget with its total, as <see cref="FilterAsync"/> totals them. Not tracked.</summary>
    /// <param name="today">Which month and year are current: monthly and yearly budgets only count their dates.</param>
    Task<List<BudgetSummary>> GetActiveSummariesAsync(DateTime today, CancellationToken cancellationToken = default);

    /// <param name="today">Which month and year are current: monthly and yearly budgets only count their dates.</param>
    Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, DateTime today, CancellationToken cancellationToken = default);
}

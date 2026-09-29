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

    /// <param name="monthStart">First day of the current month: monthly budgets only count spending from then on.</param>
    Task<BudgetSummary?> GetSummaryAsync(Guid id, DateTime monthStart, CancellationToken cancellationToken = default);

    /// <param name="monthStart">First day of the current month: monthly budgets only count spending from then on.</param>
    Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, DateTime monthStart, CancellationToken cancellationToken = default);
}

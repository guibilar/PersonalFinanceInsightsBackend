using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IBudgetRepository : IBaseRepository<Budget>
{
    /// <summary>Tracked, with its linked transactions (only deleting a budget needs them).</summary>
    Task<Budget?> GetWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<BudgetSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, CancellationToken cancellationToken = default);
}

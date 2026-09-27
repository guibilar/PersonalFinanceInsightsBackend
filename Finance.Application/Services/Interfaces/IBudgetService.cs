using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Services.Interfaces;

public interface IBudgetService
{
    Task<BudgetSummary> AddAsync(Budget budget, CancellationToken cancellationToken = default);
    Task<BudgetSummary> UpdateAsync(Budget budget, CancellationToken cancellationToken = default);
    Task<BudgetSummary> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, CancellationToken cancellationToken = default);
}



using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;

namespace Finance.Application.Services.Interfaces;

public interface IBudgetService
{
    Task<Budget> AddAsync(Budget budget);
    Task<Budget> UpdateAsync(Budget budget);
    Task<Budget?> GetByIdAsync(Guid id);
    Task RemoveAsync(Guid id);
    Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter);
}
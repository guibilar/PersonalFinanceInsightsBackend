using FinanceDomain.Models;
using FinanceDto.Inputs;
using FinanceDto.Models;

namespace CrossCutting.Services.Interfaces;

public interface IBudgetService
{
    Task<Budget> AddAsync(Budget budget);
    Task<Budget> UpdateAsync(Budget budget);
    Task<Budget?> GetByIdAsync(Guid id);
    Task RemoveAsync(Guid id);
    Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter);
}
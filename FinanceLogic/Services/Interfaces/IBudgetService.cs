using FinanceDomain.Models;

namespace CrossCutting.Services.Interfaces;

public interface IBudgetService
{
    Task<Budget> AddAsync(Budget budget);
    Task<Budget> UpdateAsync(Budget budget);
    Task<Budget?> GetByIdAsync(Guid id);
    Task RemoveAsync(Guid id);
}
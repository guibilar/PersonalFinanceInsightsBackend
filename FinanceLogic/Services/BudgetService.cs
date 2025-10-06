using CrossCutting.Services.Interfaces;
using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using FinanceDto.Models;

namespace CrossCutting.Services;

public class BudgetService(IBudgetRepository budgetRepository) : IBudgetService
{
    public async Task<Budget> AddAsync(Budget budget)
    {
        return await budgetRepository.AddAsync(budget);
    }
    
    public async Task<Budget?> GetByIdAsync(Guid id)
    {
        return await budgetRepository.GetByIdAsync(id);
    }

    public async Task<Budget> UpdateAsync(Budget budget)
    {
        return await budgetRepository.UpdateAsync(budget);
    }
    
    public async Task RemoveAsync(Guid id)
    {
        await budgetRepository.RemoveAsync(id);
    }

    public async Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter)
    {
        return await budgetRepository.FilterAsync(filter);
    }
    
    
}
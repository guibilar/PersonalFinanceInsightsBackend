

using Finance.Application.Abstractions;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services;

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
        var existing = await budgetRepository.GetByIdAsync(budget.Id)
                       ?? throw new KeyNotFoundException($"Budget with id {budget.Id} not found.");

        // Update the tracked entity so its linked transactions (and totals) are kept.
        existing.Name = budget.Name;
        existing.Description = budget.Description;
        existing.Status = budget.Status;
        existing.Amount = budget.Amount;

        return await budgetRepository.UpdateAsync(existing);
    }

    public async Task RemoveAsync(Guid id)
    {
        var budget = await budgetRepository.GetByIdAsync(id)
                     ?? throw new KeyNotFoundException($"Budget with id {id} not found.");

        // The foreign key has no ON DELETE action, so unlink transactions before deleting.
        foreach (var transaction in budget.Transactions)
        {
            transaction.BudgetId = null;
        }

        await budgetRepository.RemoveAsync(budget.Id);
    }

    public async Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter)
    {
        return await budgetRepository.FilterAsync(filter);
    }
}

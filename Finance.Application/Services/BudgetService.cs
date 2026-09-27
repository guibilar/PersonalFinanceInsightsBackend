using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class BudgetService(IBudgetRepository budgetRepository) : IBudgetService
{
    public async Task<BudgetSummary> AddAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        await budgetRepository.AddAsync(budget, cancellationToken);
        return new BudgetSummary(budget, TotalSpent: 0);
    }

    public async Task<BudgetSummary> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await budgetRepository.GetSummaryAsync(id, cancellationToken)
               ?? throw new NotFoundException(nameof(Budget), id);
    }

    public async Task<BudgetSummary> UpdateAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        var existing = await budgetRepository.GetByIdAsync(budget.Id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Budget), budget.Id);

        existing.Name = budget.Name;
        existing.Description = budget.Description;
        existing.Status = budget.Status;
        existing.Amount = budget.Amount;
        await budgetRepository.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(budget.Id, cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var budget = await budgetRepository.GetWithTransactionsAsync(id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Budget), id);

        // The foreign key has no ON DELETE action, so unlink transactions before deleting.
        foreach (var transaction in budget.Transactions)
        {
            transaction.BudgetId = null;
        }

        await budgetRepository.RemoveAsync(budget, cancellationToken);
    }

    public Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, CancellationToken cancellationToken = default)
    {
        return budgetRepository.FilterAsync(filter, cancellationToken);
    }
}

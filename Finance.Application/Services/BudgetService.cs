using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class BudgetService(IBudgetRepository budgetRepository, ReportingCalendar calendar) : IBudgetService
{
    public async Task<BudgetSummary> AddAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        await EnsureCategoriesFitAsync(budget, cancellationToken);
        await budgetRepository.AddAsync(budget, cancellationToken);
        return new BudgetSummary(budget, TotalSpent: 0, budget.Categories.Select(c => c.Category).ToList());
    }

    public async Task<BudgetSummary> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await budgetRepository.GetSummaryAsync(id, calendar.CurrentMonthStart(), cancellationToken)
               ?? throw new NotFoundException(nameof(Budget), id);
    }

    public async Task<BudgetSummary> UpdateAsync(Budget budget, CancellationToken cancellationToken = default)
    {
        var existing = await budgetRepository.GetByIdAsync(budget.Id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Budget), budget.Id);

        await EnsureCategoriesFitAsync(budget, cancellationToken);

        // Only what changed: removing and re-adding a category in one save would collide on its key.
        var wanted = budget.Categories.Select(c => c.Category).ToHashSet();
        existing.Categories.RemoveAll(c => !wanted.Contains(c.Category));
        foreach (var category in wanted.Except(existing.Categories.Select(c => c.Category)))
        {
            existing.Categories.Add(new BudgetCategory { Category = category });
        }

        existing.Name = budget.Name;
        existing.Description = budget.Description;
        existing.Status = budget.Status;
        existing.Amount = budget.Amount;
        existing.Period = budget.Period;
        await budgetRepository.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(budget.Id, cancellationToken);
    }

    /// <summary>
    /// Categories count this month's spending, so they belong to Monthly budgets; and each is counted by one budget
    /// only, or its spending would be in two budgets at once.
    /// </summary>
    private async Task EnsureCategoriesFitAsync(Budget budget, CancellationToken cancellationToken)
    {
        if (budget.Categories.Count == 0) return;

        if (budget.Categories.Any(c => !Enum.IsDefined(c.Category)))
        {
            throw new InvalidInputException("categories", "One of the categories is not a known category.");
        }

        if (budget.Period != BudgetPeriod.Monthly)
        {
            throw new InvalidInputException("categories",
                "Categories count the current month's spending, so only a monthly budget can have them.");
        }

        var categories = budget.Categories.Select(c => c.Category).Distinct().ToList();
        var owners = await budgetRepository.GetCategoryOwnersAsync(categories, budget.Id, cancellationToken);
        if (owners.Count > 0)
        {
            var (category, owner) = owners.First();
            throw new ConflictException($"{category} is already counted by the budget \"{owner}\".");
        }
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
        return budgetRepository.FilterAsync(filter, calendar.CurrentMonthStart(), cancellationToken);
    }
}

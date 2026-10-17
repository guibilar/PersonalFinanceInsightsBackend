using Finance.Application.Abstractions;
using Finance.Application.Events;
using Finance.Application.Models;
using Finance.Application.Reports;
using Finance.Application.Services.Interfaces;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class BudgetService(
    IBudgetRepository budgetRepository,
    ICategoryRepository categoryRepository,
    ReportingCalendar calendar,
    FinanceChanges changes)
    : IBudgetService
{
    // A new budget or a new limit can be over 80 % or 100 % at once: FinanceChanges sends BudgetLimitReached (T04).
    public Task<BudgetSummary> AddAsync(Budget budget, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, async ct =>
        {
            // Only the categories' ids: a failed attempt linked them to its categories, which one after a database
            // failure doesn't track (IUnitOfWork).
            budget.Categories.ForEach(c => c.Category = null);
            await EnsureFitsAsync(budget, ct);
            await budgetRepository.AddAsync(budget, ct);
            // Totalled like any other read: a new budget's categories may already have spending this month.
            return await GetByIdAsync(budget.Id, ct);
        }, cancellationToken);

    public async Task<BudgetSummary> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await budgetRepository.GetSummaryAsync(id, calendar.Today(), cancellationToken)
               ?? throw new NotFoundException(nameof(Budget), id);
    }

    public Task<BudgetSummary> UpdateAsync(Budget budget, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, ct => UpdateOnceAsync(budget, ct), cancellationToken);

    private async Task<BudgetSummary> UpdateOnceAsync(Budget budget, CancellationToken cancellationToken)
    {
        var existing = await budgetRepository.GetByIdAsync(budget.Id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Budget), budget.Id);

        await EnsureFitsAsync(budget, cancellationToken);

        // Only what changed: removing and re-adding a category in one save would collide on its key.
        var wanted = budget.Categories.Select(c => c.CategoryId).ToHashSet();
        existing.Categories.RemoveAll(c => !wanted.Contains(c.CategoryId));
        foreach (var categoryId in wanted.Except(existing.Categories.Select(c => c.CategoryId)))
        {
            existing.Categories.Add(new BudgetCategory { CategoryId = categoryId });
        }

        existing.Name = budget.Name;
        existing.Description = budget.Description;
        existing.Status = budget.Status;
        existing.Amount = budget.Amount;
        existing.Period = budget.Period;
        existing.StartDate = budget.StartDate;
        existing.EndDate = budget.EndDate;
        existing.Merchant = budget.Merchant;
        await budgetRepository.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(budget.Id, cancellationToken);
    }

    /// <summary>
    /// A custom period needs its two days. Categories and a merchant count the period's spending, so a total budget
    /// can't have them, and a budget counts one or the other; each category is counted by one budget only, or its
    /// spending would be in two budgets at once (a merchant can be in several: "No Frills" a month and a year).
    /// </summary>
    private async Task EnsureFitsAsync(Budget budget, CancellationToken cancellationToken)
    {
        if (budget.Period == BudgetPeriod.Custom)
        {
            if (budget.StartDate is null || budget.EndDate is null)
            {
                throw new InvalidInputException("startDate", "A custom period needs a start date and an end date.");
            }

            if (budget.StartDate > budget.EndDate)
            {
                throw new InvalidInputException("endDate", "The end date is before the start date.");
            }
        }

        if (budget.Merchant is not null)
        {
            if (budget.Period == BudgetPeriod.Total)
            {
                throw new InvalidInputException("merchant",
                    "A merchant counts the period's spending, so a total budget can't have one; choose a monthly, yearly or custom period.");
            }

            if (budget.Categories.Count > 0)
            {
                throw new InvalidInputException("merchant", "A budget counts a merchant or categories, not both.");
            }
        }

        if (budget.Categories.Count == 0) return;

        var ids = budget.Categories.Select(c => c.CategoryId).Distinct().ToList();
        var categories = await categoryRepository.GetByIdsAsync(ids, cancellationToken);
        if (categories.Count != ids.Count)
        {
            throw new InvalidInputException("categoryIds", "One of the categories doesn't exist.");
        }

        if (budget.Period == BudgetPeriod.Total)
        {
            throw new InvalidInputException("categoryIds",
                "Categories count the period's spending, so a total budget can't have them; choose a monthly, yearly or custom period.");
        }

        var owners = await budgetRepository.GetCategoryOwnersAsync(ids, budget.Id, cancellationToken);
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
        return budgetRepository.FilterAsync(filter, calendar.Today(), cancellationToken);
    }
}

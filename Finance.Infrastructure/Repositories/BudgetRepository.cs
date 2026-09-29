using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class BudgetRepository(FinanceDbContext dbContext) : BaseRepository<Budget>(dbContext), IBudgetRepository
{
    public Task<Budget?> GetWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Hidden transactions too: deleting the budget has to unlink every one of them.
        return DbContext.Budgets
            .IgnoreQueryFilters()
            .Include(x => x.Transactions)
            .Include(x => x.Categories)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public override Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Budgets.Include(x => x.Categories).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Dictionary<Category, string>> GetCategoryOwnersAsync(
        IReadOnlyCollection<Category> categories, Guid exceptBudgetId, CancellationToken cancellationToken = default)
    {
        var owners = await DbContext.Set<BudgetCategory>()
            .Where(c => categories.Contains(c.Category) && c.BudgetId != exceptBudgetId)
            .Select(c => new { c.Category, c.Budget!.Name })
            .ToListAsync(cancellationToken);
        return owners.ToDictionary(o => o.Category, o => o.Name);
    }

    public Task<BudgetSummary?> GetSummaryAsync(Guid id, DateTime monthStart, CancellationToken cancellationToken = default)
    {
        return ToSummaries(DbContext.Budgets.Where(x => x.Id == id), monthStart)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, DateTime monthStart, CancellationToken cancellationToken = default)
    {
        return PaginateAsync(ToSummaries(FilterQuery(filter), monthStart), filter.Page, filter.PageSize, cancellationToken);
    }

    // Totals are summed in SQL instead of loading every linked transaction. Amounts are stored positive,
    // so credits (refunds) linked to a budget give money back instead of counting as spending.
    // Monthly budgets only count this month's transactions; earlier months stay linked but no longer count.
    // A budget with categories (monthly ones only) also counts this month's spending in them that isn't linked to
    // another budget: spending as it counts in the reports, so refunds come off, and transfers and ignored rows
    // stay out.
    private IQueryable<BudgetSummary> ToSummaries(IQueryable<Budget> budgets, DateTime monthStart)
    {
        var nextMonth = monthStart.AddMonths(1);
        return budgets
            .AsNoTracking()
            .Select(b => new BudgetSummary(b, b.Transactions
                .Where(t => b.Period == BudgetPeriod.Total || (t.Date >= monthStart && t.Date < nextMonth))
                .Sum(t => t.TransactionType == TransactionType.Credit ? -t.Amount : t.Amount)
                + DbContext.Transactions
                    .Where(t => t.BudgetId == null && t.Date >= monthStart && t.Date < nextMonth
                                && (t.Flow == Flow.Spending || t.Flow == Flow.Refund)
                                && b.Categories.Any(c => c.Category == t.Category))
                    .Sum(t => t.Flow == Flow.Refund ? -t.Amount : t.Amount),
                b.Categories.Select(c => c.Category).ToList()));
    }

    private IQueryable<Budget> FilterQuery(BudgetFilterDto filter)
    {
        var query = DbContext.Budgets.AsQueryable();

        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Status == filter.Status.Value);
        }

        if (!string.IsNullOrEmpty(filter.Name))
        {
            query = query.Where(x => x.Name.ToLower().Contains(filter.Name.ToLower()));
        }

        if (!string.IsNullOrEmpty(filter.Description))
        {
            query = query.Where(x => x.Description.ToLower().Contains(filter.Description.ToLower()));
        }

        return query.OrderBy(x => x.Name);
    }
}

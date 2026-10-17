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
    // Runs of spaces as one, as the rules compare text (RuleText), in SQL: each space gets a marker after it, a marker
    // followed by a space goes, then the markers left. Npgsql's Regex.Replace replaces only the first run.
    private const string Marker = "\u0001";
    private const string Mark = " " + Marker;
    private const string Unmark = Marker + " ";

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

    public async Task<Dictionary<string, string>> GetCategoryOwnersAsync(
        IReadOnlyCollection<Guid> categoryIds, Guid exceptBudgetId, CancellationToken cancellationToken = default)
    {
        var owners = await DbContext.Set<BudgetCategory>()
            .Where(c => categoryIds.Contains(c.CategoryId) && c.BudgetId != exceptBudgetId)
            .Select(c => new { Category = c.Category!.Name, Budget = c.Budget!.Name })
            .ToListAsync(cancellationToken);
        return owners.ToDictionary(o => o.Category, o => o.Budget);
    }

    public Task<BudgetSummary?> GetSummaryAsync(Guid id, DateTime today, CancellationToken cancellationToken = default)
    {
        return ToSummaries(DbContext.Budgets.Where(x => x.Id == id), today)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<List<BudgetSummary>> GetActiveSummariesAsync(DateTime today, CancellationToken cancellationToken = default)
    {
        return ToSummaries(DbContext.Budgets.Where(x => x.Status == Status.Active), today).ToListAsync(cancellationToken);
    }

    public Task<PaginatedList<BudgetSummary>> FilterAsync(BudgetFilterDto filter, DateTime today, CancellationToken cancellationToken = default)
    {
        return PaginateAsync(ToSummaries(FilterQuery(filter), today), filter.Page, filter.PageSize, cancellationToken);
    }

    // Totals are summed in SQL instead of loading every linked transaction. Amounts are stored positive,
    // so credits (refunds) linked to a budget give money back instead of counting as spending. Linked transfers and
    // ignored rows stay out, as they do everywhere else: paying the card isn't spending.
    // A period limits which transactions count by their date: the current month, the current year, or the budget's
    // own days (both ends included); earlier ones stay linked but no longer count. A total budget has no limit.
    // A budget with a period also counts the period's spending in its categories, or at its merchant (part of the
    // description, ignoring case and taking runs of spaces as one), that isn't linked to another budget: spending as it
    // counts in the reports, so refunds come off, and transfers and ignored rows stay out.
    private IQueryable<BudgetSummary> ToSummaries(IQueryable<Budget> budgets, DateTime today)
    {
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = monthStart.AddMonths(1);
        var yearStart = new DateTime(today.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextYear = yearStart.AddYears(1);
        return budgets
            .AsNoTracking()
            .Select(b => new
            {
                Budget = b,
                From = b.Period == BudgetPeriod.Monthly ? monthStart
                    : b.Period == BudgetPeriod.Yearly ? yearStart
                    : b.Period == BudgetPeriod.Custom ? b.StartDate
                    : (DateTime?)null,
                // Exclusive: the day after the last one.
                To = b.Period == BudgetPeriod.Monthly ? nextMonth
                    : b.Period == BudgetPeriod.Yearly ? nextYear
                    : b.Period == BudgetPeriod.Custom ? b.EndDate!.Value.AddDays(1)
                    : (DateTime?)null,
            })
            .Select(w => new BudgetSummary(w.Budget, w.Budget.Transactions
                .Where(t => (w.From == null || t.Date >= w.From) && (w.To == null || t.Date < w.To)
                            && t.Flow != Flow.Transfer && t.Flow != Flow.Ignored)
                .Sum(t => t.TransactionType == TransactionType.Credit ? -t.Amount : t.Amount)
                + DbContext.Transactions
                    .Where(t => t.BudgetId == null && w.From != null && t.Date >= w.From && t.Date < w.To
                                && (t.Flow == Flow.Spending || t.Flow == Flow.Refund)
                                && (w.Budget.Categories.Any(c => c.CategoryId == t.CategoryId)
                                    || (w.Budget.Merchant != null
                                        && t.Description.Replace(" ", Mark).Replace(Unmark, "").Replace(Marker, "").ToLower()
                                            .Contains(w.Budget.Merchant.Replace(" ", Mark).Replace(Unmark, "").Replace(Marker, "").ToLower()))))
                    .Sum(t => t.Flow == Flow.Refund ? -t.Amount : t.Amount),
                w.Budget.Categories.Select(c => c.Category!).OrderBy(c => c.Name).ToList()));
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

        // Then by id: names can repeat, and a page boundary between two of them would otherwise list one twice or not at all.
        return query.OrderBy(x => x.Name).ThenBy(x => x.Id);
    }
}

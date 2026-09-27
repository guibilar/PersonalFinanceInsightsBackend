using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using FinanceDto.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceDomain.Repositories;

public class BudgetRepository(FinanceDbContext dbContext) : BaseRepository<Budget>(dbContext), IBudgetRepository
{
    // Totals are computed from the linked transactions, so always load them.
    public override async Task<Budget?> GetByIdAsync(Guid id)
    {
        return await DbContext.Budgets
            .Include(x => x.Transactions)
            .SingleOrDefaultAsync(x => x.Id == id);
    }

    public async Task<PaginatedList<Budget>> FilterAsync(BudgetFilterDto? filter)
    {
        filter ??= new BudgetFilterDto();

        var query = FilterQuery(filter);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PaginatedList<Budget>
        {
            Items = items,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalCount = totalCount
        };
    }

    private IQueryable<Budget> FilterQuery(BudgetFilterDto filter)
    {
        var query = DbContext.Budgets
            .Include(x => x.Transactions)
            .AsQueryable();

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

        query = query.OrderBy(x => x.Name);
        return query;
    }
}
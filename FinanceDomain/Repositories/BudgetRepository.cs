using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using FinanceDto.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceDomain.Repositories;

public class BudgetRepository(FinanceDbContext dbContext) : BaseRepository<Budget>(dbContext), IBudgetRepository
{
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
        var query = dbContext.Set<Budget>().AsQueryable();
        
        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Status == filter.Status.Value);
        }

        if (!string.IsNullOrEmpty(filter.Name))
        {
            query = query.Where(x => x.Name.ToLower().Contains(filter.Name.ToLower()));
        }

        query = query.OrderBy(x => x.Name);
        return query;
    }
}
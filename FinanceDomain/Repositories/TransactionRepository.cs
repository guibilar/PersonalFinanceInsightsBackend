using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using FinanceDto.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceDomain.Repositories;

public class TransactionRepository(FinanceDbContext dbContext)
    : BaseRepository<Transaction>(dbContext), ITransactionRepository
{
    public override async Task<List<Transaction>> GetAllAsync()
    {
        return await DbContext.Transactions.OrderByDescending(x => x.Date).ToListAsync();
    }

    public async Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto? filter)
    {
        filter ??= new TransactionFilterDto();

        var query = FilterNoPaginationQuery(filter);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PaginatedList<Transaction>
        {
            Items = items,
            Page = filter.Page,
            PageSize = filter.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto? filter)
    {
        filter ??= new TransactionFilterDto();

        var query = FilterNoPaginationQuery(filter);

        return await query.ToListAsync();
    }

    private IQueryable<Transaction> FilterNoPaginationQuery(TransactionFilterDto filter)
    {
        var query = dbContext.Transactions
            .Include(x => x.Budget)
            .AsQueryable();

        if (filter.TransactionId.HasValue)
        {
            query = query.Where(x => x.Id == filter.TransactionId.Value);
        }

        if (filter.TransactionType.HasValue)
        {
            query = query.Where(x => x.TransactionType == filter.TransactionType.Value);
        }

        if (filter.AccountType is { Count: > 0 })
        {
            query = query.Where(x => filter.AccountType.Contains(x.Account));
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(x => x.Date >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(x => x.Date <= filter.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(filter.Description))
        {
            query = query.Where(x =>
                x.Description.ToLower() == filter.Description.ToLower());
        }

        if (filter.Category.HasValue)
        {
            query = query.Where(x => x.Category == filter.Category.Value);
        }

        if (filter.BudgetId.HasValue)
        {
            query = query.Where(x => x.BudgetId == filter.BudgetId.Value);
        }

        query = query.OrderByDescending(x => x.Date).ThenBy(x => x.Id);
        return query;
    }

    public async Task RemoveAsync(Guid id)
    {
        var transaction = await dbContext.Transactions.FindAsync(id);
        if (transaction != null)
        {
            dbContext.Transactions.Remove(transaction);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task<Transaction?> GetByHashAsync(string hash)
    {
        return await dbContext.Transactions.SingleOrDefaultAsync(t => t.Hash == hash);
    }
}
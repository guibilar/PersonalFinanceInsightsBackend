using Finance.Application.Abstractions;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class TransactionRepository(FinanceDbContext dbContext)
    : BaseRepository<Transaction>(dbContext), ITransactionRepository
{
    public Task<List<Transaction>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions
            .AsNoTracking()
            .Include(x => x.Budget)
            .OrderByDescending(x => x.Date)
            .ToListAsync(cancellationToken);
    }

    public Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default)
    {
        return PaginateAsync(FilterQuery(filter), filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default)
    {
        return FilterQuery(filter).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.AnyAsync(t => t.Hash == hash, cancellationToken);
    }

    private IQueryable<Transaction> FilterQuery(TransactionFilterDto filter)
    {
        var query = DbContext.Transactions
            .AsNoTracking()
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
            query = query.Where(x => x.Date < filter.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(filter.Description))
        {
            query = query.Where(x => x.Description.ToLower().Contains(filter.Description.ToLower()));
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
}

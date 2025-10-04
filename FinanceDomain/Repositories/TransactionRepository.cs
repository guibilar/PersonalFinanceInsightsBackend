using FinanceDomain.Models;
using FinanceDomain.Repositories.Interfaces;
using FinanceDto.Inputs;
using Microsoft.EntityFrameworkCore;

namespace FinanceDomain.Repositories;

public class TransactionRepository(FinanceDbContext dbContext) : ITransactionRepository
{
    public async Task<Transaction> AddAsync(Transaction transaction)
    {
        await dbContext.Transactions.AddAsync(transaction);
        await dbContext.SaveChangesAsync();
        return transaction;
    }
    
    public async Task<Transaction?> GetByIdAsync(Guid id)
    {
        return await dbContext.Transactions.SingleOrDefaultAsync(t => t.Id == id);
    }

    public async Task<List<Transaction>> GetAllAsync()
    {
        return await dbContext.Transactions.OrderByDescending(x => x.Date).ToListAsync();
    }

    public async Task<List<Transaction>> FilterAsync(TransactionFilterDto? filter)
    {
        var query = dbContext.Transactions.AsQueryable();

        if (filter == null) return await query.ToListAsync();

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

        return await query.OrderByDescending(x => x.Date).ToListAsync();
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
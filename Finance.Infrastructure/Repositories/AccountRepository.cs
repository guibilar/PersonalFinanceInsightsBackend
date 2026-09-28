using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class AccountRepository(FinanceDbContext dbContext) : BaseRepository<Account>(dbContext), IAccountRepository
{
    public async Task<List<Account>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return (await DbContext.Accounts.ToListAsync(cancellationToken))
            .OrderBy(a => a.Type).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<AccountSummary>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        var counts = await DbContext.Transactions.IgnoreQueryFilters()
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, cancellationToken);
        var accounts = await DbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        // Sorted here: the type is stored by name, and the list should follow the enum's order.
        return accounts
            .OrderBy(a => a.Type).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccountSummary(a, counts.GetValueOrDefault(a.Id)))
            .ToList();
    }

    public Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.IgnoreQueryFilters().CountAsync(t => t.AccountId == id, cancellationToken);
    }
}

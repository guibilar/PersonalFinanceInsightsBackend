using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
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

    public async Task<List<AccountSummary>> GetSummariesAsync(int year, CancellationToken cancellationToken = default)
    {
        var accounts = await DbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        return await SummariesAsync(accounts, year, cancellationToken);
    }

    public async Task<AccountSummary> GetSummaryAsync(Account account, int year, CancellationToken cancellationToken = default)
    {
        return (await SummariesAsync([account], year, cancellationToken)).Single();
    }

    private async Task<List<AccountSummary>> SummariesAsync(List<Account> accounts, int year, CancellationToken cancellationToken)
    {
        var ids = accounts.Select(a => a.Id).ToList();
        var counts = await DbContext.Transactions.IgnoreQueryFilters()
            .Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count(), Last = g.Max(t => t.Date) })
            .ToDictionaryAsync(x => x.AccountId, cancellationToken);
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var transfers = await DbContext.Transactions
            .Where(t => ids.Contains(t.AccountId) && t.Flow == Flow.Transfer && t.Date >= start && t.Date < start.AddYears(1))
            // A transfer straight from or to another plan of the same kind is not a contribution or a withdrawal.
            .Where(t => !t.Description.StartsWith("TRFINTF ") && !t.Description.StartsWith("TRFOUTTF ") && !t.Description.StartsWith("WIREINTF "))
            .GroupBy(t => new { t.AccountId, t.TransactionType })
            .Select(g => new { g.Key.AccountId, g.Key.TransactionType, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);
        var balances = (await DbContext.AccountBalances.AsNoTracking().Where(b => ids.Contains(b.AccountId)).ToListAsync(cancellationToken))
            .GroupBy(b => b.AccountId)
            .ToDictionary(g => g.Key, g => g.MaxBy(b => b.Date)!);
        decimal Total(Guid id, TransactionType type) =>
            transfers.Where(t => t.AccountId == id && t.TransactionType == type).Sum(t => t.Total);

        // Sorted here: the type is stored by name, and the list should follow the enum's order.
        return accounts
            .OrderBy(a => a.Type).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(a => new AccountSummary(a, counts.GetValueOrDefault(a.Id)?.Count ?? 0, balances.GetValueOrDefault(a.Id),
                Total(a.Id, TransactionType.Credit), Total(a.Id, TransactionType.Debit), counts.GetValueOrDefault(a.Id)?.Last))
            .ToList();
    }

    public Task<List<AccountBalance>> GetBalancesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return DbContext.AccountBalances.AsNoTracking().Where(b => b.AccountId == accountId)
            .OrderByDescending(b => b.Date).ToListAsync(cancellationToken);
    }

    public Task<List<AccountBalance>> GetAllBalancesAsync(CancellationToken cancellationToken = default)
    {
        return DbContext.AccountBalances.AsNoTracking().OrderBy(b => b.Date).ToListAsync(cancellationToken);
    }

    public Task<AccountBalance?> GetBalanceAsync(Guid accountId, DateTime date, CancellationToken cancellationToken = default)
    {
        return DbContext.AccountBalances.SingleOrDefaultAsync(b => b.AccountId == accountId && b.Date == date, cancellationToken);
    }

    public Task<AccountBalance?> GetBalanceByIdAsync(Guid accountId, Guid balanceId, CancellationToken cancellationToken = default)
    {
        return DbContext.AccountBalances.SingleOrDefaultAsync(b => b.AccountId == accountId && b.Id == balanceId, cancellationToken);
    }

    public async Task AddBalanceAsync(AccountBalance balance, CancellationToken cancellationToken = default)
    {
        DbContext.AccountBalances.Add(balance);
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveBalanceAsync(AccountBalance balance, CancellationToken cancellationToken = default)
    {
        DbContext.AccountBalances.Remove(balance);
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>With its balances: loaded first, so they go with it on any provider, not only where the database cascades.</summary>
    public override async Task RemoveAsync(Account entity, CancellationToken cancellationToken = default)
    {
        await DbContext.AccountBalances.Where(b => b.AccountId == entity.Id).LoadAsync(cancellationToken);
        await base.RemoveAsync(entity, cancellationToken);
    }

    public Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.IgnoreQueryFilters().CountAsync(t => t.AccountId == id, cancellationToken);
    }
}

using Finance.Application.Abstractions;
using Finance.Contracts.Inputs;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class TransactionRepository(FinanceDbContext dbContext)
    : BaseRepository<Transaction>(dbContext), ITransactionRepository
{
    // First half of the advisory lock key for imports (the account is the second); any constant unique to this app.
    private const int ImportLockKey = 0x46494e41; // "FINA"

    public Task<PaginatedList<Transaction>> FilterAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default)
    {
        return PaginateAsync(FilterQuery(filter), filter.Page, filter.PageSize, cancellationToken);
    }

    public Task<List<Transaction>> FilterNoPaginationAsync(TransactionFilterDto filter, CancellationToken cancellationToken = default)
    {
        return FilterQuery(filter).ToListAsync(cancellationToken);
    }

    // Tracked, with its budget so the response after an edit still shows the link.
    public override Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.Include(t => t.Budget).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public Task<List<Transaction>> GetRuleCategorizedAsync(CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions
            .Where(t => !t.CategoryLocked && t.Account != AccountType.Savings)
            .ToListAsync(cancellationToken);
    }

    public Task<Transaction?> GetIncludingHiddenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.IgnoreQueryFilters().Include(t => t.Budget)
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public Task<Transaction?> FindByHashAsync(AccountType account, string hash, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Account == account && t.Hash == hash, cancellationToken);
    }

    public Task<int> AddMissingAsync(
        AccountType account, IReadOnlyCollection<Transaction> transactions, CancellationToken cancellationToken = default)
    {
        // The retrying execution strategy only allows an explicit transaction when it can replay all of it.
        return DbContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            DbContext.ChangeTracker.Clear();
            // The in-memory test provider has no transactions; it runs the same steps without isolation.
            await using var dbTransaction = DbContext.Database.IsRelational()
                ? await DbContext.Database.BeginTransactionAsync(ct)
                : null;
            if (DbContext.Database.IsNpgsql())
            {
                // Held until commit: a concurrent import for the same account waits, then sees these rows as existing.
                // Without it, both would insert the same rows and fail on the unique index (or deadlock on it).
                await DbContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ImportLockKey}, {(int)account})", ct);
            }

            var hashes = transactions.Select(t => t.Hash).ToList();
            // Hidden rows count: they were deleted on purpose and must not come back.
            var existing = (await DbContext.Transactions
                .IgnoreQueryFilters()
                .Where(t => t.Account == account && hashes.Contains(t.Hash))
                .Select(t => t.Hash)
                .ToListAsync(ct)).ToHashSet();
            var toAdd = transactions.Where(t => !existing.Contains(t.Hash)).ToList();

            DbContext.Transactions.AddRange(toAdd);
            await SaveChangesAsync(ct);
            if (dbTransaction is not null) await dbTransaction.CommitAsync(ct);
            return toAdd.Count;
        }, cancellationToken);
    }

    private IQueryable<Transaction> FilterQuery(TransactionFilterDto filter)
    {
        var query = filter.Hidden
            ? DbContext.Transactions.IgnoreQueryFilters().Where(x => x.Hidden)
            : DbContext.Transactions;
        query = query
            .AsNoTracking()
            .Include(x => x.Budget);

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

        return Sort(query, filter.SortBy, filter.SortDirection);
    }

    private static IQueryable<Transaction> Sort(IQueryable<Transaction> query, TransactionSortField field, SortDirection direction)
    {
        var ascending = direction == SortDirection.Ascending;
        IOrderedQueryable<Transaction> ordered = field switch
        {
            TransactionSortField.Description => ascending ? query.OrderBy(x => x.Description) : query.OrderByDescending(x => x.Description),
            TransactionSortField.Amount => ascending ? query.OrderBy(x => x.Amount) : query.OrderByDescending(x => x.Amount),
            // Stored as text, so this sorts by category name.
            TransactionSortField.Category => ascending ? query.OrderBy(x => x.Category) : query.OrderByDescending(x => x.Category),
            _ => ascending ? query.OrderBy(x => x.Date) : query.OrderByDescending(x => x.Date),
        };

        if (field != TransactionSortField.Date)
        {
            ordered = ordered.ThenByDescending(x => x.Date);
        }
        return ordered.ThenBy(x => x.Id);
    }
}

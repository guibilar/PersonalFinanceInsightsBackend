using Finance.Application.Abstractions;
using Finance.Domain.Common;
using Finance.Domain.Entities;
using Finance.Domain.Exceptions;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Infrastructure.Repositories;

public class BaseRepository<T>(FinanceDbContext dbContext) : IBaseRepository<T> where T : BaseEntity
{
    protected readonly FinanceDbContext DbContext = dbContext;

    public async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await DbContext.Set<T>().AddAsync(entity, cancellationToken);
        await SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Set<T>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public virtual async Task RemoveAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbContext.Set<T>().Remove(entity);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent request stored the same record first; the failed additions stay tracked, so drop them.
            DetachUnsaved();
            throw new DuplicateEntityException($"An identical {typeof(T).Name.ToLowerInvariant()} already exists.");
        }
    }

    /// <summary>
    /// Drops entities queued for insert that a failed save left tracked, so they aren't sent again with the next save.
    /// Everything else stays tracked: an entity read earlier in the request can still be changed and saved.
    /// </summary>
    protected void DetachUnsaved()
    {
        foreach (var entry in DbContext.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    protected static async Task<PaginatedList<TItem>> PaginateAsync<TItem>(
        IQueryable<TItem> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<TItem>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}

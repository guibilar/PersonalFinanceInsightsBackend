using Microsoft.EntityFrameworkCore;
using Finance.Application.Abstractions;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;

namespace Finance.Infrastructure.Repositories;

public class BaseRepository<T>(FinanceDbContext dbContext) : IBaseRepository<T> where T : BaseEntity
{
    protected readonly FinanceDbContext DbContext = dbContext;

    public async Task<T> AddAsync(T entity)
    {
        await DbContext.Set<T>().AddAsync(entity);
        await DbContext.SaveChangesAsync();
        return entity;
    }

    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        return await DbContext.Set<T>().SingleOrDefaultAsync(e => e.Id == id);
    }

    public virtual async Task<List<T>> GetAllAsync()
    {
        return await DbContext.Set<T>().ToListAsync();
    }

    public async Task<T> UpdateAsync(T entity)
    {
        DbContext.Set<T>().Update(entity);
        await DbContext.SaveChangesAsync();
        return entity;
    }

    public async Task RemoveAsync(Guid id)
    {
        var entity = await DbContext.Set<T>().FindAsync(id);
        if (entity != null)
        {
            DbContext.Set<T>().Remove(entity);
            await DbContext.SaveChangesAsync();
        }
    }
}

using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface IBaseRepository<T> where T : BaseEntity
{
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Tracked: changes are saved by <see cref="SaveChangesAsync"/>.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task RemoveAsync(T entity, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

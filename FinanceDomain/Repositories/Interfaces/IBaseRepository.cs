using FinanceDomain.Models;

namespace FinanceDomain.Repositories.Interfaces;

public interface IBaseRepository<T> where T : BaseEntity
{
    Task<T> AddAsync(T entity);
    Task<T?> GetByIdAsync(Guid id);
    Task<List<T>> GetAllAsync();
    Task<T> UpdateAsync(T entity);
    Task RemoveAsync(Guid id);
}

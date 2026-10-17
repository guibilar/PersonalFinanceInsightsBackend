using Finance.Application.Models;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface ICategoryRepository : IBaseRepository<Category>
{
    /// <summary>Tracked, by name.</summary>
    Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>By name, with how many transactions (deleted ones included) and rules use each.</summary>
    Task<List<CategorySummary>> GetSummariesAsync(CancellationToken cancellationToken = default);

    Task<CategorySummary> GetSummaryAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>The ones of these ids that exist, by name.</summary>
    Task<List<Category>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the category after moving its transactions (deleted ones included) and rules to <paramref name="targetId"/>;
    /// budgets that counted it stop counting it. All of it or nothing.
    /// </summary>
    /// <returns>The transactions moved.</returns>
    Task<List<Transaction>> RemoveMovingUsageAsync(Category category, Guid targetId, CancellationToken cancellationToken = default);
}

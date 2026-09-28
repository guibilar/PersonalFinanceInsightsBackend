using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface ICategoryRuleRepository : IBaseRepository<CategoryRule>
{
    /// <summary>Tracked, by position.</summary>
    Task<List<CategoryRule>> GetAllAsync(CancellationToken cancellationToken = default);
}

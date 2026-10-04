using Finance.Application.Models;
using Finance.Domain.Entities;

namespace Finance.Application.Abstractions;

public interface ITagRepository : IBaseRepository<Tag>
{
    /// <summary>Tracked, by name.</summary>
    Task<List<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>By name, with how many transactions (deleted ones aside) have each.</summary>
    Task<List<TagSummary>> GetSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>The ones of these ids that exist, tracked.</summary>
    Task<List<Tag>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default);
}

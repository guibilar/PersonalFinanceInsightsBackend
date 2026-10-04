using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class TagRepository(FinanceDbContext dbContext) : BaseRepository<Tag>(dbContext), ITagRepository
{
    public async Task<List<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ByName(await DbContext.Tags.ToListAsync(cancellationToken));
    }

    public async Task<List<TagSummary>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        var tags = await DbContext.Tags.AsNoTracking()
            .Select(t => new { Tag = t, Count = t.Transactions.Count(x => !x.Hidden) })
            .ToListAsync(cancellationToken);
        return tags.OrderBy(t => t.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TagSummary(t.Tag, t.Count))
            .ToList();
    }

    public async Task<List<Tag>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        return ByName(await DbContext.Tags.Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken));
    }

    public Task<int> CountTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return DbContext.Transactions.CountAsync(t => t.Tags.Any(tag => tag.Id == id), cancellationToken);
    }

    private static List<Tag> ByName(List<Tag> tags) => tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
}

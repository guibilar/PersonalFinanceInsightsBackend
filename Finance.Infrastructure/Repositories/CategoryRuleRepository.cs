using Finance.Application.Abstractions;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class CategoryRuleRepository(FinanceDbContext dbContext)
    : BaseRepository<CategoryRule>(dbContext), ICategoryRuleRepository
{
    public Task<List<CategoryRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return DbContext.CategoryRules.Include(r => r.Category).OrderBy(r => r.Position).ThenBy(r => r.Keyword).ToListAsync(cancellationToken);
    }
}

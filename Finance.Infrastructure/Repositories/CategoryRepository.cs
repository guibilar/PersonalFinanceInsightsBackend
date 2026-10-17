using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Finance.Infrastructure.Repositories;

public class CategoryRepository(FinanceDbContext dbContext) : BaseRepository<Category>(dbContext), ICategoryRepository
{
    public async Task<List<Category>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ByName(await DbContext.Categories.ToListAsync(cancellationToken));
    }

    public async Task<List<CategorySummary>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        var transactions = await DbContext.Transactions
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);
        var rules = await DbContext.CategoryRules
            .GroupBy(r => r.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);
        var categories = await DbContext.Categories.AsNoTracking().ToListAsync(cancellationToken);
        return ByName(categories)
            .Select(c => new CategorySummary(c, transactions.GetValueOrDefault(c.Id), rules.GetValueOrDefault(c.Id)))
            .ToList();
    }

    public async Task<CategorySummary> GetSummaryAsync(Category category, CancellationToken cancellationToken = default)
    {
        var transactions = await DbContext.Transactions.CountAsync(t => t.CategoryId == category.Id, cancellationToken);
        var rules = await DbContext.CategoryRules.CountAsync(r => r.CategoryId == category.Id, cancellationToken);
        return new CategorySummary(category, transactions, rules);
    }

    public async Task<List<Category>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        return ByName(await DbContext.Categories.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken));
    }

    public async Task<List<Transaction>> RemoveMovingUsageAsync(Category category, Guid targetId, CancellationToken cancellationToken = default)
    {
        // Loaded rather than updated in bulk, so the in-memory test database does the same; one save stores it all.
        // Hidden transactions move too: they are kept for duplicate detection and can be restored.
        var transactions = await DbContext.Transactions.IgnoreQueryFilters()
            .Where(t => t.CategoryId == category.Id)
            .ToListAsync(cancellationToken);
        transactions.ForEach(t => t.CategoryId = targetId);

        var rules = await DbContext.CategoryRules.Where(r => r.CategoryId == category.Id).ToListAsync(cancellationToken);
        rules.ForEach(r => r.CategoryId = targetId);

        // The database cascades this too; removed here so every provider agrees.
        DbContext.Set<BudgetCategory>().RemoveRange(
            await DbContext.Set<BudgetCategory>().Where(c => c.CategoryId == category.Id).ToListAsync(cancellationToken));

        DbContext.Categories.Remove(category);
        await SaveChangesAsync(cancellationToken);
        return transactions;
    }

    // In memory: ordering by name ignoring case is the same everywhere, unlike the database's collation.
    private static List<Category> ByName(IEnumerable<Category> categories) =>
        categories.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
}

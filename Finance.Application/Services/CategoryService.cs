using Finance.Application.Abstractions;
using Finance.Application.Events;
using Finance.Application.Models;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Exceptions;
using Hestia.Contracts.Finance;

namespace Finance.Application.Services;

public class CategoryService(ICategoryRepository categoryRepository, FinanceChanges changes) : ICategoryService
{
    public Task<List<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        categoryRepository.GetSummariesAsync(cancellationToken);

    public async Task<CategorySummary> AddAsync(string name, string color, CancellationToken cancellationToken = default)
    {
        var category = new Category { Name = name.Trim(), Color = NormalizeColor(color) };
        EnsureUnique(await categoryRepository.GetAllAsync(cancellationToken), category.Name, category.Id);
        await categoryRepository.AddAsync(category, cancellationToken);
        return new CategorySummary(category, 0, 0);
    }

    public async Task<CategorySummary> UpdateAsync(Guid id, string name, string color, CancellationToken cancellationToken = default)
    {
        var categories = await categoryRepository.GetAllAsync(cancellationToken);
        var category = categories.SingleOrDefault(c => c.Id == id) ?? throw new NotFoundException(nameof(Category), id);
        var trimmed = name.Trim();
        EnsureUnique(categories, trimmed, id);

        category.Name = trimmed;
        category.Color = NormalizeColor(color);
        await categoryRepository.SaveChangesAsync(cancellationToken);
        return await categoryRepository.GetSummaryAsync(category, cancellationToken);
    }

    // The moved transactions are changed ones (T04), and can take a budget of the category they move to past 80 % or 100 %.
    public Task RemoveAsync(Guid id, Guid? moveTo, CancellationToken cancellationToken = default) =>
        changes.RunAsync(ChangeEffect.Spending, ct => RemoveOnceAsync(id, moveTo, ct), cancellationToken);

    private async Task RemoveOnceAsync(Guid id, Guid? moveTo, CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(id, cancellationToken)
                       ?? throw new NotFoundException(nameof(Category), id);
        if (CategorySummary.BuiltInReasonFor(category.Id) is { } reason)
        {
            throw new ConflictException($"\"{category.Name}\" can't be deleted. {reason}");
        }

        var targetId = moveTo ?? Category.OtherId;
        if (targetId == id)
        {
            throw new InvalidInputException("moveTo", "Choose another category for its transactions and rules.");
        }
        if (await categoryRepository.GetByIdAsync(targetId, cancellationToken) is null)
        {
            throw new InvalidInputException("moveTo", "The category to move its transactions and rules to doesn't exist.");
        }

        var moved = await categoryRepository.RemoveMovingUsageAsync(category, targetId, cancellationToken);
        // As for the rules applied again: the events are of those the list shows.
        await changes.RaiseChangedAsync(moved.Where(t => !t.Hidden).ToList(), TransactionChange.Updated, cancellationToken);
    }

    private static string NormalizeColor(string color) => color.Trim().ToUpperInvariant();

    // The database's unique index is the backstop for concurrent requests; this gives the usual case a clear message.
    private static void EnsureUnique(List<Category> categories, string name, Guid id)
    {
        if (categories.Any(c => c.Id != id && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"A category named \"{name}\" already exists.");
        }
    }
}

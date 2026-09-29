using Finance.Application.Models;

namespace Finance.Application.Services.Interfaces;

/// <summary>The categories transactions, rules and budgets use. Names are unique ignoring case.</summary>
public interface ICategoryService
{
    /// <summary>By name.</summary>
    Task<List<CategorySummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <param name="color">"#RRGGBB"; stored upper-case.</param>
    Task<CategorySummary> AddAsync(string name, string color, CancellationToken cancellationToken = default);

    Task<CategorySummary> UpdateAsync(Guid id, string name, string color, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the category, moving its transactions and rules to <paramref name="moveTo"/> (Other when null). Other
    /// and Rent can't be deleted.
    /// </summary>
    Task RemoveAsync(Guid id, Guid? moveTo, CancellationToken cancellationToken = default);
}

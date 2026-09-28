using Finance.Application.Models;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Services.Interfaces;

/// <summary>The category rules used at import and by re-applying the rules; positions always run 0..n-1.</summary>
public interface ICategoryRuleService
{
    /// <summary>By position (the order they are checked in).</summary>
    Task<List<CategoryRule>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <param name="position">Where to insert it (the rules from there on move down); at the end when null.</param>
    Task<CategoryRule> AddAsync(string keyword, Category category, int? position, CancellationToken cancellationToken = default);

    Task<CategoryRule> UpdateAsync(Guid id, string keyword, Category category, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Moves the rule to <paramref name="position"/> (past the end means last) and returns all rules.</summary>
    Task<List<CategoryRule>> MoveAsync(Guid id, int position, CancellationToken cancellationToken = default);

    /// <summary>
    /// What adding this rule first and re-applying the rules would change: only transactions the rules decide
    /// (see <see cref="ITransactionService.ApplyCategoryRulesAsync"/>).
    /// </summary>
    Task<CategoryRulePreview> PreviewAsync(string keyword, Category category, Guid? excludeTransactionId,
        CancellationToken cancellationToken = default);
}

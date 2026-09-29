using Finance.Domain.Entities;

namespace Finance.Application.Models;

/// <param name="TransactionCount">Deleted (hidden) ones included: deleting the category moves them too.</param>
public record CategorySummary(Category Category, int TransactionCount, int RuleCount)
{
    /// <summary>Why the category can't be deleted, for the two the app relies on; null for the rest.</summary>
    public string? BuiltInReason => BuiltInReasonFor(Category.Id);

    public static string? BuiltInReasonFor(Guid categoryId) => categoryId == Category.OtherId
        ? "Built in: transactions that no rule matches go here."
        : categoryId == Category.RentId
            ? "Built in: a rent payment in it on chequing replaces the generated monthly rent."
            : null;
}

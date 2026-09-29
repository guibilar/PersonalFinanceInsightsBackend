using Finance.Application.Models;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;

namespace FinanceApi.Mapping;

public static class CategoryMapping
{
    public static CategoryOutputDto ToOutput(this CategorySummary summary)
    {
        return new CategoryOutputDto
        {
            Id = summary.Category.Id,
            Name = summary.Category.Name,
            Color = summary.Category.Color,
            TransactionCount = summary.TransactionCount,
            RuleCount = summary.RuleCount,
            BuiltInReason = summary.BuiltInReason
        };
    }

    /// <param name="category">Loaded with its owner; a missing one is a query that forgot to include it.</param>
    public static CategoryReferenceDto ToReference(this Category? category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return new CategoryReferenceDto { Id = category.Id, Name = category.Name, Color = category.Color };
    }
}

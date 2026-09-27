using Finance.Application.Models;
using Finance.Contracts.Outputs;
using Finance.Domain.Entities;

namespace FinanceApi.Mapping;

public static class CategoryRuleMapping
{
    public static CategoryRuleOutputDto ToOutput(this CategoryRule rule)
    {
        return new CategoryRuleOutputDto { Id = rule.Id, Keyword = rule.Keyword, Category = rule.Category, Position = rule.Position };
    }

    public static CategoryRulePreviewDto ToOutput(this CategoryRulePreview preview)
    {
        return new CategoryRulePreviewDto { Count = preview.Count, Examples = preview.Examples };
    }
}

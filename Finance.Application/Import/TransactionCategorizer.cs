using Finance.Domain.Enums;

namespace Finance.Application.Import;

public static class TransactionCategorizer
{
    public static Category Categorize(string description, ImportOptions options)
    {
        var upper = description.ToUpperInvariant();
        foreach (var rule in options.CategoryRules)
        {
            if (rule.Keywords.Any(keyword => upper.Contains(keyword, StringComparison.Ordinal)))
            {
                return rule.Category;
            }
        }
        return Category.Other;
    }
}

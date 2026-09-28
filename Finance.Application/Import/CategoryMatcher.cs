using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Import;

/// <summary>Picks a transaction's category from the category rules: the first rule whose keyword the description contains.</summary>
public class CategoryMatcher
{
    private readonly List<(string Keyword, Category Category)> _rules;

    /// <param name="rules">In any order; they are checked by position.</param>
    public CategoryMatcher(IEnumerable<CategoryRule> rules)
    {
        _rules = rules.OrderBy(rule => rule.Position)
            .Select(rule => (CategoryRule.NormalizeKeyword(rule.Keyword), rule.Category))
            .Where(rule => rule.Item1.Length > 0)
            .ToList();
    }

    public Category Categorize(string description)
    {
        var upper = description.ToUpperInvariant();
        foreach (var (keyword, category) in _rules)
        {
            if (upper.Contains(keyword, StringComparison.Ordinal))
            {
                return category;
            }
        }
        return Category.Other;
    }
}

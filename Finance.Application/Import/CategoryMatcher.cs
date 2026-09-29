using Finance.Domain.Entities;

namespace Finance.Application.Import;

/// <summary>Picks a transaction's category from the category rules: the first rule whose keyword the description contains.</summary>
public class CategoryMatcher
{
    private readonly List<(string Keyword, Guid CategoryId)> _rules;

    /// <param name="rules">In any order; they are checked by position.</param>
    public CategoryMatcher(IEnumerable<CategoryRule> rules)
    {
        _rules = rules.OrderBy(rule => rule.Position)
            .Select(rule => (CategoryRule.NormalizeKeyword(rule.Keyword), rule.CategoryId))
            .Where(rule => rule.Item1.Length > 0)
            .ToList();
    }

    /// <returns>The matching rule's category, or Other when none matches.</returns>
    public Guid Categorize(string description)
    {
        var upper = description.ToUpperInvariant();
        foreach (var (keyword, categoryId) in _rules)
        {
            if (upper.Contains(keyword, StringComparison.Ordinal))
            {
                return categoryId;
            }
        }
        return Category.OtherId;
    }
}

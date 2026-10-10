using Finance.Domain.Entities;
using Finance.Domain.Helpers;

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
    public Guid Categorize(string description) => Match(description)?.CategoryId ?? Category.OtherId;

    /// <summary>The keyword of the rule that decides this description (the merchant, usually); null when none matches.</summary>
    public string? MatchingKeyword(string description) => Match(description)?.Keyword;

    private (string Keyword, Guid CategoryId)? Match(string description)
    {
        var upper = RuleText.Of(description);
        foreach (var rule in _rules)
        {
            if (upper.Contains(rule.Keyword, StringComparison.Ordinal))
            {
                return rule;
            }
        }
        return null;
    }
}

using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Import;

/// <summary>Rules for turning bank statements into transactions (section "Import" in import-rules.json).</summary>
public class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>
    /// The initial category rules: keywords per category, in order (a list, because configuration sorts
    /// dictionary keys). The API copies them into the database once, where they are then edited in the app;
    /// only the CLI, which has no database, still categorises with these.
    /// </summary>
    public List<CategoryKeywords> CategoryRules { get; set; } = [];

    /// <summary>
    /// The initial flow rules (transfers, card payments, income...): keywords per flow, copied into the database
    /// once like <see cref="CategoryRules"/>.
    /// </summary>
    public List<FlowKeywords> FlowRules { get; set; } = [];

    /// <summary>
    /// Extra starter rules for one bank or use, each copied into the database once, after the rules it already has
    /// (see <see cref="RulePack"/>). Adding a pack here reaches databases that were seeded before it existed.
    /// </summary>
    public List<RulePack> RulePacks { get; set; } = [];

    /// <summary>
    /// <see cref="CategoryRules"/> as one rule per keyword, in order (rule order, then keyword order). A keyword
    /// that appears again later is left out: the first one always wins anyway.
    /// </summary>
    /// <param name="categoryId">
    /// The category a name in the file stands for, or null when there is none (its rules are then left out).
    /// </param>
    public List<CategoryRule> ToCategoryRules(Func<string, Guid?> categoryId) =>
        CategoryKeywords.ToRules(CategoryRules, new HashSet<string>(), 0, categoryId);

    /// <summary>
    /// <see cref="FlowRules"/> as one rule per keyword. A keyword that appears again later is left out, and so are
    /// spending rules: money out is spending unless a rule says otherwise.
    /// </summary>
    public List<FlowRule> ToFlowRules()
    {
        var keywords = new HashSet<string>();
        return FlowRules
            .Where(rule => rule.Flow != Flow.Spending)
            .SelectMany(rule => rule.Keywords.Select(keyword => (Keyword: FlowRule.NormalizeKeyword(keyword), rule.Flow)))
            .Where(rule => rule.Keyword.Length > 0 && keywords.Add(rule.Keyword))
            .Select(rule => new FlowRule { Keyword = rule.Keyword, Flow = rule.Flow })
            .ToList();
    }
}

/// <summary>
/// A named set of starter rules (Neo Financial's merchants, say). Its rules are added to the database once, after the
/// existing ones and skipping keywords that already have a rule, so nothing edited in the app is ever overridden.
/// </summary>
public class RulePack
{
    public string Name { get; set; } = string.Empty;
    public List<CategoryKeywords> CategoryRules { get; set; } = [];
    public List<FlowKeywords> FlowRules { get; set; } = [];

    /// <summary>
    /// One rule per keyword, positioned from <paramref name="firstPosition"/> on; keywords in <paramref name="existing"/>
    /// are left out, and so are rules for a category name <paramref name="categoryId"/> doesn't know.
    /// </summary>
    public List<CategoryRule> ToCategoryRules(ISet<string> existing, int firstPosition, Func<string, Guid?> categoryId) =>
        CategoryKeywords.ToRules(CategoryRules, new HashSet<string>(existing), firstPosition, categoryId);

    /// <summary>One rule per keyword; keywords in <paramref name="existing"/> and spending rules are left out.</summary>
    public List<FlowRule> ToFlowRules(ISet<string> existing)
    {
        var seen = new HashSet<string>(existing);
        return FlowRules
            .Where(rule => rule.Flow != Flow.Spending)
            .SelectMany(rule => rule.Keywords.Select(keyword => (Keyword: FlowRule.NormalizeKeyword(keyword), rule.Flow)))
            .Where(rule => rule.Keyword.Length > 0 && seen.Add(rule.Keyword))
            .Select(rule => new FlowRule { Keyword = rule.Keyword, Flow = rule.Flow })
            .ToList();
    }
}

/// <summary>An initial category rule from import-rules.json: any of the keywords gives the category.</summary>
public class CategoryKeywords
{
    /// <summary>The category's name (one of the default categories', see <see cref="DefaultCategories"/>).</summary>
    public string Category { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = [];

    /// <summary>One rule per keyword, skipping keywords already in <paramref name="seen"/> and unknown categories.</summary>
    internal static List<CategoryRule> ToRules(IEnumerable<CategoryKeywords> rules, HashSet<string> seen, int firstPosition,
        Func<string, Guid?> categoryId) =>
        rules
            .Select(rule => (Rule: rule, CategoryId: categoryId(rule.Category)))
            .Where(rule => rule.CategoryId is not null)
            .SelectMany(rule => rule.Rule.Keywords.Select(keyword => (Keyword: CategoryRule.NormalizeKeyword(keyword), CategoryId: rule.CategoryId!.Value)))
            .Where(rule => rule.Keyword.Length > 0 && seen.Add(rule.Keyword))
            .Select((rule, index) => new CategoryRule { Keyword = rule.Keyword, CategoryId = rule.CategoryId, Position = firstPosition + index })
            .ToList();
}

/// <summary>An initial flow rule from import-rules.json: any of the keywords gives the flow.</summary>
public class FlowKeywords
{
    public Flow Flow { get; set; }
    public List<string> Keywords { get; set; } = [];
}

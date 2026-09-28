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
    /// <see cref="CategoryRules"/> as one rule per keyword, in order (rule order, then keyword order). A keyword
    /// that appears again later is left out: the first one always wins anyway.
    /// </summary>
    public List<CategoryRule> ToCategoryRules()
    {
        var keywords = new HashSet<string>();
        return CategoryRules
            .SelectMany(rule => rule.Keywords.Select(keyword => (Keyword: CategoryRule.NormalizeKeyword(keyword), rule.Category)))
            .Where(rule => rule.Keyword.Length > 0 && keywords.Add(rule.Keyword))
            .Select((rule, position) => new CategoryRule { Keyword = rule.Keyword, Category = rule.Category, Position = position })
            .ToList();
    }

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

/// <summary>An initial category rule from import-rules.json: any of the keywords gives the category.</summary>
public class CategoryKeywords
{
    public Category Category { get; set; }
    public List<string> Keywords { get; set; } = [];
}

/// <summary>An initial flow rule from import-rules.json: any of the keywords gives the flow.</summary>
public class FlowKeywords
{
    public Flow Flow { get; set; }
    public List<string> Keywords { get; set; } = [];
}

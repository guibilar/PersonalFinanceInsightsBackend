using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Application.Import;

/// <summary>Rules for turning bank statements into transactions (section "Import" in import-rules.json).</summary>
public class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>Rows whose description contains any of these (e.g. transfers between own accounts) are skipped.</summary>
    public List<string> ExcludedDescriptions { get; set; } = [];

    /// <summary>
    /// Money in on a credit card statement is a refund, except the card's own payments, which contain one of these
    /// (case-insensitive) and are skipped: the money already left the chequing account as a transfer.
    /// </summary>
    public List<string> CardPaymentKeywords { get; set; } = [];

    /// <summary>Deposits on a chequing statement are imported as income only when they contain one of these.</summary>
    public List<string> IncomeKeywords { get; set; } = [];

    /// <summary>
    /// The initial category rules: keywords per category, in order (a list, because configuration sorts
    /// dictionary keys). The API copies them into the database once, where they are then edited in the app;
    /// only the CLI, which has no database, still categorises with these.
    /// </summary>
    public List<CategoryKeywords> CategoryRules { get; set; } = [];

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
}

/// <summary>An initial category rule from import-rules.json: any of the keywords gives the category.</summary>
public class CategoryKeywords
{
    public Category Category { get; set; }
    public List<string> Keywords { get; set; } = [];
}

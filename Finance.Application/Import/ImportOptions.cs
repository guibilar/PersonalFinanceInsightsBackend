using System.Globalization;
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
    /// When set, rent payments found in statements are replaced by one fixed rent transaction per month on
    /// chequing statements.
    /// </summary>
    public MonthlyRentOptions? MonthlyRent { get; set; }

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
    /// The rent the importer adds each month: Rent by definition, whatever its description says to the rules.
    /// </summary>
    public bool IsFixedMonthlyRent(Transaction transaction) =>
        MonthlyRent is { } rent &&
        transaction is { Account: AccountType.Chequing, Category: Category.Rent } &&
        transaction.Description == rent.Description;
}

/// <summary>An initial category rule from import-rules.json: any of the keywords gives the category.</summary>
public class CategoryKeywords
{
    public Category Category { get; set; }
    public List<string> Keywords { get; set; } = [];
}

public class MonthlyRentOptions
{
    /// <summary>The rent before the first of <see cref="Changes"/> (or always, without changes).</summary>
    public decimal Amount { get; set; }
    public string Description { get; set; } = "Monthly Rent";

    /// <summary>
    /// Rent changes, each from a month on ("yyyy-MM"), in any order. Add one when the rent goes up instead of
    /// editing <see cref="Amount"/>, so months before the change keep the amount that was actually paid.
    /// </summary>
    public List<RentChange> Changes { get; set; } = [];

    /// <summary>The rent for the month starting on <paramref name="month"/>.</summary>
    public decimal AmountFor(DateTime month) =>
        Changes.Where(change => change.FromMonth <= month)
            .OrderBy(change => change.FromMonth)
            .Select(change => (decimal?)change.Amount)
            .LastOrDefault() ?? Amount;

    public bool IsValid() => Changes.All(change => change.IsValid());
}

public class RentChange
{
    /// <summary>First month with this amount, as "yyyy-MM".</summary>
    public string From { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public DateTime FromMonth => DateTime.ParseExact(From, "yyyy-MM", CultureInfo.InvariantCulture);

    public bool IsValid() =>
        Amount > 0 && DateTime.TryParseExact(From, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

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
    /// Upper-case keywords per category. Rules are checked in order (a list, because configuration sorts
    /// dictionary keys); the first match wins and no match means <see cref="Category.Other"/>.
    /// </summary>
    public List<CategoryRule> CategoryRules { get; set; } = [];

    /// <summary>
    /// When set, rent payments found in statements are replaced by one fixed rent transaction per month on
    /// chequing statements.
    /// </summary>
    public MonthlyRentOptions? MonthlyRent { get; set; }
}

public class CategoryRule
{
    public Category Category { get; set; }
    public List<string> Keywords { get; set; } = [];
}

public class MonthlyRentOptions
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = "Monthly Rent";
}

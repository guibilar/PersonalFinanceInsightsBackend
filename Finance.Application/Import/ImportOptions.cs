using System.Globalization;
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

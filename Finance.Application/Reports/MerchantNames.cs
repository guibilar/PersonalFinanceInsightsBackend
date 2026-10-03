using System.Text.RegularExpressions;
using Finance.Application.Import;

namespace Finance.Application.Reports;

/// <summary>Who a bank description is from, so the charges of one merchant group together whatever store or reference they carry.</summary>
public static partial class MerchantNames
{
    /// <summary>As many as a category rule's keyword takes.</summary>
    private const int MaxLength = 100;

    // Card processors put their own name before the merchant's: "SQ *CORNER STORE", "TST-PIZZA PLACE".
    [GeneratedRegex(@"^(SQ\s*\*|TST\s*[-*]|PAYPAL\s*\*|PP\s*\*|SP\s+)\s*")]
    private static partial Regex ProcessorPrefix();

    /// <summary>
    /// The start of a description, as a rule keyword would be: the processor prefix dropped, then the words up to the
    /// first with a digit or '#', upper case. "SQ *CORNER STORE #123 TORONTO ON" gives "CORNER STORE"; the whole
    /// description when nothing is left ("7-ELEVEN 1234").
    /// </summary>
    public static string Normalize(string description)
    {
        var upper = description.Trim().ToUpperInvariant();
        var words = ProcessorPrefix().Replace(upper, "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var end = Array.FindIndex(words, w => w.Any(c => char.IsDigit(c) || c == '#'));
        var keyword = string.Join(' ', end < 0 ? words : words.Take(end)).Trim();
        var name = keyword.Length > 0 ? keyword : string.Join(' ', upper.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length > MaxLength ? name[..MaxLength].Trim() : name;
    }

    /// <summary>The merchant: the keyword of the category rule that decides the description, or its <see cref="Normalize"/>d start.</summary>
    public static string Of(CategoryMatcher matcher, string description) =>
        matcher.MatchingKeyword(description) ?? Normalize(description);
}

using Finance.Domain.Enums;
using Finance.Domain.Helpers;

namespace Finance.Domain.Entities;

/// <summary>
/// A transaction whose description contains <see cref="Keyword"/> (ignoring case) gets <see cref="Flow"/>; when several
/// rules match, the longest keyword wins. Income and refund rules only apply to money in.
/// </summary>
public class FlowRule : BaseEntity
{
    /// <summary>Trimmed and upper-case, unique.</summary>
    public required string Keyword { get; set; }

    /// <summary>Never <see cref="Enums.Flow.Spending"/>: that is what money out is when no rule says otherwise.</summary>
    public Flow Flow { get; set; }

    public static string NormalizeKeyword(string keyword) => RuleText.Of(keyword);
}

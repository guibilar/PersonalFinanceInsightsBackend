using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

/// <summary>
/// A transaction whose description contains <see cref="Keyword"/> (ignoring case) gets <see cref="Category"/>.
/// Rules are checked by <see cref="Position"/>; the first match wins.
/// </summary>
public class CategoryRule : BaseEntity
{
    /// <summary>Trimmed and upper-case, unique.</summary>
    public required string Keyword { get; set; }
    public Category Category { get; set; }

    /// <summary>0 for the first rule checked; the positions of all rules are always 0..n-1.</summary>
    public int Position { get; set; }

    public static string NormalizeKeyword(string keyword) => keyword.Trim().ToUpperInvariant();
}

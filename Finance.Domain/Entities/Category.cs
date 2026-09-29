namespace Finance.Domain.Entities;

/// <summary>What a transaction was spent on. Categories are data: added, renamed, recoloured and deleted in the app.</summary>
public class Category : BaseEntity
{
    /// <summary>Where transactions go that no category rule matches (and savings movements). Can't be deleted.</summary>
    public static readonly Guid OtherId = DefaultCategories.Other;

    /// <summary>A rent payment in it on chequing replaces the generated monthly rent (see LegacyMonthlyRent). Can't be deleted.</summary>
    public static readonly Guid RentId = DefaultCategories.Rent;

    /// <summary>Trimmed, unique ignoring case.</summary>
    public required string Name { get; set; }

    /// <summary>"#RRGGBB", upper-case: the colour it has in lists and charts.</summary>
    public required string Color { get; set; }

    /// <summary>Other and Rent, which the app itself relies on.</summary>
    public bool IsBuiltIn => Id == OtherId || Id == RentId;
}

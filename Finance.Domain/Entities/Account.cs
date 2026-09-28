using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

/// <summary>A bank account or card that statements are imported into.</summary>
public class Account : BaseEntity
{
    // The accounts every database starts with; before accounts were data, these were the only three.
    public static readonly Guid DefaultChequingId = new("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a01");
    public static readonly Guid DefaultSavingsId = new("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a02");
    public static readonly Guid DefaultCreditCardId = new("5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03");

    /// <summary>Trimmed, unique ignoring case.</summary>
    public required string Name { get; set; }

    /// <summary>Decides how its statements are read and what its transactions mean by default. Never changes.</summary>
    public AccountType Type { get; set; }
}

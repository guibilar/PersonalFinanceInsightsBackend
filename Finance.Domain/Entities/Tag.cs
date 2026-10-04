namespace Finance.Domain.Entities;

/// <summary>
/// A label for transactions across categories ("trip-portugal", "tax-deductible"): any number of them per transaction,
/// to find and total them together.
/// </summary>
public class Tag : BaseEntity
{
    public required string Name { get; set; }
    public List<Transaction> Transactions { get; set; } = [];
}



using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

public class Transaction : BaseEntity
{
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public Category Category { get; set; }
    public TransactionType? TransactionType { get; set; }
    public required AccountType Account { get; set; }
    public string Hash { get; set; } = string.Empty;

    /// <summary>Set when someone picks the category by hand; re-applying the category rules leaves it alone.</summary>
    public bool CategoryLocked { get; set; }
    public Guid? BudgetId { get; set; }
    public Budget? Budget { get; set; }
}
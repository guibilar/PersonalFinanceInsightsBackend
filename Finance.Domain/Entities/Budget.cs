using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

public class Budget : BaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Status Status { get; set; }
    public decimal Amount { get; set; }
    public BudgetPeriod Period { get; set; }
    public List<Transaction> Transactions { get; set; } = [];

    /// <summary>Monthly budgets only: what this month's spending in these categories adds to the linked transactions.</summary>
    public List<BudgetCategory> Categories { get; set; } = [];
}

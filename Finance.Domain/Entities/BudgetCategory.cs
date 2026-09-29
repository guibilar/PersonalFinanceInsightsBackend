namespace Finance.Domain.Entities;

/// <summary>
/// A category a budget counts by itself: its spending in the budget's period needs no linking. The category is the key,
/// so a category belongs to at most one budget.
/// </summary>
public class BudgetCategory
{
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    public Guid BudgetId { get; set; }
    public Budget? Budget { get; set; }
}

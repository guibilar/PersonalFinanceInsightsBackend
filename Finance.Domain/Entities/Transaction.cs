using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

public class Transaction : BaseEntity
{
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Other until the rules or a person decide.</summary>
    public Guid CategoryId { get; set; } = Entities.Category.OtherId;
    public Category? Category { get; set; }
    public TransactionType? TransactionType { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public string Hash { get; set; } = string.Empty;

    /// <summary>Set when someone picks the category by hand; re-applying the category rules leaves it alone.</summary>
    public bool CategoryLocked { get; set; }

    /// <summary>Whether it counts as spending, income or neither (see <see cref="FlowRule"/>).</summary>
    public Flow Flow { get; set; }

    /// <summary>Set when someone picks the flow by hand; re-applying the flow rules leaves it alone.</summary>
    public bool FlowLocked { get; set; }

    /// <summary>
    /// Deleted by the user. The row stays so re-importing a statement that contains it doesn't add it back;
    /// it is left out of lists, reports and budget totals until restored.
    /// </summary>
    public bool Hidden { get; set; }
    public Guid? BudgetId { get; set; }
    public Budget? Budget { get; set; }

    /// <summary>The person's own words about it ("Gift for mom"); searched along with the description.</summary>
    public string? Note { get; set; }

    public List<Tag> Tags { get; set; } = [];
}

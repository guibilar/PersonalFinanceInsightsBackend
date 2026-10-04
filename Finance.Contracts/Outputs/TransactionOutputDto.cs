using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class TransactionOutputDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public required CategoryReferenceDto Category { get; set; }
    public bool CategoryLocked { get; set; }
    public bool Hidden { get; set; }
    public TransactionType? TransactionType { get; set; }
    public Guid AccountId { get; set; }
    public required string AccountName { get; set; }
    public AccountType AccountType { get; set; }
    public Flow Flow { get; set; }
    public bool FlowLocked { get; set; }
    public Guid? BudgetId { get; set; }
    public BudgetReferenceDto? Budget { get; set; }
    public string? Note { get; set; }

    /// <summary>By name.</summary>
    public List<TagReferenceDto> Tags { get; set; } = [];
}

using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class TransactionOutputDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public Category Category { get; set; }
    public bool CategoryLocked { get; set; }
    public bool Hidden { get; set; }
    public TransactionType? TransactionType { get; set; }
    public AccountType Account { get; set; }
    public Guid? BudgetId { get; set; }
    public BudgetReferenceDto? Budget { get; set; }
}

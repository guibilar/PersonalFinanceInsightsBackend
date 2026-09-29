using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class BudgetOutputDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Status Status { get; set; }
    public BudgetPeriod Period { get; set; }
    public List<Category> Categories { get; set; } = [];
    public decimal TotalAmount { get; set; }
    public decimal TotalRemaining { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal PercentageUsed { get; set; }
}

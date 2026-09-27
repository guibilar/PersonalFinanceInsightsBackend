

using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class BudgetOutputDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public Status Status { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalRemaining { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal PercentageUsed { get; set; }
    public List<TransactionOutputDto> Transactions { get; set; }
}
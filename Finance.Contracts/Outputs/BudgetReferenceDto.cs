using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

/// <summary>The budget a transaction is linked to, without totals.</summary>
public class BudgetReferenceDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Status Status { get; set; }
}

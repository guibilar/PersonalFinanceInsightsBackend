using Finance.Domain.Enums;

namespace Finance.Contracts.Outputs;

public class BudgetOutputDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Status Status { get; set; }
    public BudgetPeriod Period { get; set; }
    /// <summary>Custom periods only: the first day that counts.</summary>
    public DateTime? StartDate { get; set; }
    /// <summary>Custom periods only: the last day that counts.</summary>
    public DateTime? EndDate { get; set; }
    /// <summary>The merchant it counts by itself (any period but Total): part of the description.</summary>
    public string? Merchant { get; set; }
    /// <summary>The categories it counts by itself (any period but Total), by name.</summary>
    public List<CategoryReferenceDto> Categories { get; set; } = [];
    public decimal TotalAmount { get; set; }
    public decimal TotalRemaining { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal PercentageUsed { get; set; }
}

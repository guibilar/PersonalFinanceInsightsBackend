using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

public class Budget : BaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Status Status { get; set; }
    public decimal Amount { get; set; }
    public BudgetPeriod Period { get; set; }

    /// <summary>Custom periods only: the first day that counts.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Custom periods only: the last day that counts.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Every period but Total: spending whose description contains this (a merchant, or a chain: "NO FRILLS") counts
    /// by itself, without linking. A budget counts a merchant or categories, not both.
    /// </summary>
    public string? Merchant { get; set; }
    public List<Transaction> Transactions { get; set; } = [];

    /// <summary>Every period but Total: what the period's spending in these categories adds to the linked transactions.</summary>
    public List<BudgetCategory> Categories { get; set; } = [];
}

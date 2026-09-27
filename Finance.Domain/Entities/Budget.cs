using Finance.Domain.Enums;

namespace Finance.Domain.Entities;

public class Budget : BaseEntity
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Status Status { get; set; }
    public decimal Amount { get; set; }
    public List<Transaction> Transactions { get; set; } = [];
}

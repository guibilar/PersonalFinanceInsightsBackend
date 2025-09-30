using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class TransactionFilterDto
{
    public Guid? TransactionId { get; set; }
    public TransactionType? TransactionType { get; set; }
    public List<AccountType>? AccountType { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public Category? Category { get; set; }
}
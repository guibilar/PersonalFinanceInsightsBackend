using FinanceDto.Enums;

namespace FinanceDto.Outputs;

public class TransactionOutputDto
{
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public Category Category { get; set; }
    public TransactionType? TransactionType { get; set; }
    public AccountType Account { get; set; }
}
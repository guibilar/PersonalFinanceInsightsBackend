using FinanceDto.Enums;

namespace FinanceDomain.Models;

public class Transaction : BaseEntity
{
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public Category Category { get; set; }
    public TransactionType? TransactionType { get; set; }
    public required AccountType Account { get; set; }
    public string Hash { get; set; } = string.Empty; 
}
using System.ComponentModel.DataAnnotations.Schema;
using FinanceDto.Enums;
using FinanceDto.Helper;

namespace FinanceDto.Inputs;

public class TransactionInputDto
{
    public TransactionInputDto()
    {
        this.Hash = TransactionHashHelper.GenerateTransactionHash(this.Amount, this.Date, this.Description);
    }
    
    public DateTime Date { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public Category Category { get; set; }
    public TransactionType? TransactionType { get; set; }
    public required AccountType Account { get; set; }

    [NotMapped] 
    public required string Hash { get; set; }
}
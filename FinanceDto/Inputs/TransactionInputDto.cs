using System.ComponentModel.DataAnnotations;
using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class TransactionInputDto
{
    public DateTime Date { get; set; }

    [Required]
    public required string Description { get; set; }

    public decimal Amount { get; set; }
    public Category Category { get; set; }
    public TransactionType? TransactionType { get; set; }
    public required AccountType Account { get; set; }
}

using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class TransactionInputDto
{
    public string date { get; set; }
    public decimal amount { get; set; }
    public string description { get; set; }
    public TransactionType type { get; set; }
}
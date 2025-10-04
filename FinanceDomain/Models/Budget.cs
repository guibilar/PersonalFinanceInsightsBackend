using FinanceDto.Enums;

namespace FinanceDomain.Models;

public class Budget : BaseEntity
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Status Status { get; set; }
    public List<Transaction> Transactions { get; set; }
}
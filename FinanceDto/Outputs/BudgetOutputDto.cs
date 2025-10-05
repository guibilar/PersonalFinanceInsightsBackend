using FinanceDto.Enums;

namespace FinanceDto.Outputs;

public class BudgetOutputDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public Status Status { get; set; }
    public List<TransactionOutputDto> Transactions { get; set; }
}
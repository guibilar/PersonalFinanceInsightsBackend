using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class BudgetInputDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Status Status { get; set; }
}
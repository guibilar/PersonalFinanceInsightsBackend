using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class BudgetFilterDto
{
    public string? Name { get; set; }
    public Status? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

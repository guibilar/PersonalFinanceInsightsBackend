using System.ComponentModel.DataAnnotations;
using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class BudgetFilterDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Status? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, PagingLimits.MaxPageSize)]
    public int PageSize { get; set; } = 10;
}

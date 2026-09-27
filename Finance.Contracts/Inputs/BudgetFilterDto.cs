using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class BudgetFilterDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Status? Status { get; set; }

    [Range(1, PagingLimits.MaxPage)]
    public int Page { get; set; } = 1;

    [Range(1, PagingLimits.MaxPageSize)]
    public int PageSize { get; set; } = 10;
}

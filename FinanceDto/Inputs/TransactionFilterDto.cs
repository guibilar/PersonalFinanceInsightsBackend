using System.ComponentModel.DataAnnotations;
using FinanceDto.Enums;

namespace FinanceDto.Inputs;

public class TransactionFilterDto
{
    public Guid? TransactionId { get; set; }
    public TransactionType? TransactionType { get; set; }
    public List<AccountType>? AccountType { get; set; }
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive: transactions on this date are returned.</summary>
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public Category? Category { get; set; }
    public Guid? BudgetId { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, PagingLimits.MaxPageSize)]
    public int PageSize { get; set; } = 10;
}
using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

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

    /// <summary>Defaults to newest first. Ties are ordered by date (newest first), then id, so paging is stable.</summary>
    public TransactionSortField SortBy { get; set; } = TransactionSortField.Date;
    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    [Range(1, PagingLimits.MaxPage)]
    public int Page { get; set; } = 1;

    [Range(1, PagingLimits.MaxPageSize)]
    public int PageSize { get; set; } = 10;
}
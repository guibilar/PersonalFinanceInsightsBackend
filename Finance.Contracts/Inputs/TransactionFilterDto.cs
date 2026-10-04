using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class TransactionFilterDto
{
    public Guid? TransactionId { get; set; }
    public TransactionType? TransactionType { get; set; }
    /// <summary>Kinds of account (every account of those kinds).</summary>
    public List<AccountType>? AccountType { get; set; }
    public Guid? AccountId { get; set; }
    public Flow? Flow { get; set; }
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive: transactions on this date are returned.</summary>
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BudgetId { get; set; }
    /// <summary>Only the transactions with this tag.</summary>
    public Guid? TagId { get; set; }
    /// <summary>True: only hidden (deleted) transactions, to restore one. False (the default): only visible ones.</summary>
    public bool Hidden { get; set; }

    /// <summary>Defaults to newest first. Ties are ordered by date (newest first), then id, so paging is stable.</summary>
    public TransactionSortField SortBy { get; set; } = TransactionSortField.Date;
    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    [Range(1, PagingLimits.MaxPage)]
    public int Page { get; set; } = 1;

    [Range(1, PagingLimits.MaxPageSize)]
    public int PageSize { get; set; } = 10;
}
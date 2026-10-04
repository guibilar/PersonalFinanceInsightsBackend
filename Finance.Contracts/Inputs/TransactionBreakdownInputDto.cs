using System.ComponentModel.DataAnnotations;
using Finance.Contracts.Reports;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>The transaction list's criteria (all optional), without its paging and sorting.</summary>
public class TransactionCriteriaInputDto
{
    /// <summary>Part of the description (or the note), ignoring case: a merchant, say.</summary>
    [MaxLength(TextLimits.TransactionDescription)]
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }
    public Guid? AccountId { get; set; }

    /// <summary>Only the transactions with this tag.</summary>
    public Guid? TagId { get; set; }

    [EnumDataType(typeof(Flow))]
    public Flow? Flow { get; set; }

    /// <summary>Inclusive. Without it, the periods start at the first matching transaction.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive, as in the list. Without it, the periods end at the last matching transaction.</summary>
    public DateTime? EndDate { get; set; }
}

/// <summary>The transaction list's criteria, and how long each period is.</summary>
public class TransactionBreakdownInputDto : TransactionCriteriaInputDto
{
    [EnumDataType(typeof(BreakdownInterval))]
    public BreakdownInterval Interval { get; set; } = BreakdownInterval.Month;
}

/// <summary>The transaction list's criteria, and which page of the merchants they spent at.</summary>
public class MerchantsInputDto : TransactionCriteriaInputDto
{
    [Range(1, PagingLimits.MaxPage)]
    public int Page { get; set; } = 1;

    [Range(1, PagingLimits.MaxPageSize)]
    public int PageSize { get; set; } = 8;
}

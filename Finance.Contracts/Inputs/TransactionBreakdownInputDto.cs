using System.ComponentModel.DataAnnotations;
using Finance.Contracts.Reports;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>The transaction list's criteria (all optional), and how long each period is.</summary>
public class TransactionBreakdownInputDto
{
    /// <summary>Part of the description, ignoring case: a merchant, say.</summary>
    [MaxLength(TextLimits.TransactionDescription)]
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }
    public Guid? AccountId { get; set; }

    [EnumDataType(typeof(Flow))]
    public Flow? Flow { get; set; }

    /// <summary>Inclusive. Without it, the periods start at the first matching transaction.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Inclusive, as in the list. Without it, the periods end at the last matching transaction.</summary>
    public DateTime? EndDate { get; set; }

    [EnumDataType(typeof(BreakdownInterval))]
    public BreakdownInterval Interval { get; set; } = BreakdownInterval.Month;
}

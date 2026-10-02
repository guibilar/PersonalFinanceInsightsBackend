using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class BudgetInputDto
{
    [Required, MaxLength(TextLimits.BudgetName)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(TextLimits.BudgetDescription)]
    public string Description { get; set; } = string.Empty;

    public Status Status { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335"), Cents]
    public decimal Amount { get; set; }

    /// <summary>Optional; <see cref="BudgetPeriod.Total"/> when omitted.</summary>
    [EnumDataType(typeof(BudgetPeriod))]
    public BudgetPeriod Period { get; set; }

    /// <summary>Custom periods only (and required for them): the first day that counts.</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>Custom periods only (and required for them): the last day that counts, after or on the start.</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Categories the budget counts by itself: the period's spending in them, with no linking. Any period but
    /// Total, and a category belongs to one budget at most. Not with a merchant.
    /// </summary>
    public List<Guid> CategoryIds { get; set; } = [];

    /// <summary>
    /// A merchant the budget counts by itself: the period's spending whose description contains this, ignoring case
    /// ("no frills" for every store of the chain). Any period but Total. Not with categories.
    /// </summary>
    [MaxLength(TextLimits.BudgetMerchant)]
    public string? Merchant { get; set; }
}

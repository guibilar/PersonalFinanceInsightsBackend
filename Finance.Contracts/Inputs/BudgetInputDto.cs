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

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Amount { get; set; }

    /// <summary>Optional; <see cref="BudgetPeriod.Total"/> when omitted.</summary>
    [EnumDataType(typeof(BudgetPeriod))]
    public BudgetPeriod Period { get; set; }

    /// <summary>
    /// Categories the budget counts by itself: this month's spending in them, with no linking. Monthly budgets
    /// only, and a category belongs to one budget at most.
    /// </summary>
    public List<Guid> CategoryIds { get; set; } = [];
}

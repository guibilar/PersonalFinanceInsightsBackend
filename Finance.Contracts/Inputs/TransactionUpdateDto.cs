using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>
/// The parts of a transaction that can be corrected after import. Date, amount and account come from the bank,
/// and the duplicate-detection hash keeps its imported value so re-importing the statement still matches.
/// </summary>
public class TransactionUpdateDto
{
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.TransactionDescription)]
    public required string Description { get; set; }

    [Required]
    public Guid? CategoryId { get; set; }

    /// <summary>Unchanged when omitted. Spending only for money out, income and refunds only for money in.</summary>
    [EnumDataType(typeof(Flow))]
    public Flow? Flow { get; set; }

    /// <summary>Unchanged when omitted; empty clears it.</summary>
    [MaxLength(TextLimits.TransactionNote)]
    public string? Note { get; set; }

    /// <summary>All its tags (replacing the ones it has); unchanged when omitted, none when empty.</summary>
    public List<Guid>? TagIds { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>
/// A manually entered transaction. Date, description, amount, type and account are required: an omitted value
/// would otherwise default silently (amount 0, no type), and a transaction without a type never appears in the
/// reports. The category and flow are optional: left out, the rules decide, as for an imported row.
/// </summary>
public class TransactionInputDto
{
    /// <summary>
    /// The calendar date as the client wrote it: a time or offset is accepted but ignored, so
    /// "2026-09-01T23:30-04:00" is September 1st, not September 2nd in UTC.
    /// </summary>
    [Required]
    public DateTimeOffset? Date { get; set; }

    [Required, MaxLength(TextLimits.TransactionDescription)]
    public required string Description { get; set; }

    /// <summary>Always positive; <see cref="TransactionType"/> says which way the money moved.</summary>
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335"), Cents]
    public decimal Amount { get; set; }

    /// <summary>Picked by hand when given, so re-applying the rules keeps it.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Picked by hand when given, so re-applying the rules keeps it. Must fit the direction of the money.</summary>
    [EnumDataType(typeof(Flow))]
    public Flow? Flow { get; set; }

    [Required, EnumDataType(typeof(TransactionType))]
    public TransactionType? TransactionType { get; set; }

    [Required]
    public Guid? AccountId { get; set; }

    /// <summary>
    /// Adds the transaction although an identical one (same account, day, amount and description) exists, like
    /// two coffees at the same price on the same day.
    /// </summary>
    public bool AllowDuplicate { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>
/// A manually entered transaction. Everything is required: an omitted value would otherwise default silently
/// (amount 0, the first category, no type), and a transaction without a type never appears in the reports.
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
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Amount { get; set; }

    [Required, EnumDataType(typeof(Category))]
    public Category? Category { get; set; }

    [Required, EnumDataType(typeof(TransactionType))]
    public TransactionType? TransactionType { get; set; }

    [Required]
    public Guid? AccountId { get; set; }
}

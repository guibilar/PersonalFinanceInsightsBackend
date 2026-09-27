using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

/// <summary>
/// A manually entered transaction. Everything is required: an omitted value would otherwise default silently
/// (amount 0, the first category, no type), and a transaction without a type never appears in the reports.
/// </summary>
public class TransactionInputDto
{
    [Required]
    public DateTime? Date { get; set; }

    [Required]
    public required string Description { get; set; }

    /// <summary>Always positive; <see cref="TransactionType"/> says which way the money moved.</summary>
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335")]
    public decimal Amount { get; set; }

    [Required, EnumDataType(typeof(Category))]
    public Category? Category { get; set; }

    [Required, EnumDataType(typeof(TransactionType))]
    public TransactionType? TransactionType { get; set; }

    [EnumDataType(typeof(AccountType))]
    public required AccountType Account { get; set; }
}

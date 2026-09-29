using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class AccountUpdateDto
{
    /// <summary>Stored trimmed; unique ignoring case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.AccountName)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The layout of the bank's statement files. Unchanged when omitted.</summary>
    [EnumDataType(typeof(StatementFormat))]
    public StatementFormat? Format { get; set; }
}

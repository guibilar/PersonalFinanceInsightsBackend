using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class AccountUpdateDto
{
    /// <summary>Stored trimmed; unique ignoring case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.AccountName)]
    public string Name { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class AccountInputDto : AccountUpdateDto
{
    /// <summary>Can't be changed later: it decides what the account's transactions mean.</summary>
    [Required, EnumDataType(typeof(AccountType))]
    public AccountType? Type { get; set; }

    // Format (from the update body) defaults to CIBC when a new account doesn't say: the only format there was.
}

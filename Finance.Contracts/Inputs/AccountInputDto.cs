using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class AccountInputDto : AccountUpdateDto
{
    /// <summary>Can't be changed later: it decides what the account's transactions mean.</summary>
    [Required, EnumDataType(typeof(AccountType))]
    public AccountType? Type { get; set; }
}

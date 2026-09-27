using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class ChangePasswordInputDto
{
    [Required]
    public required string CurrentPassword { get; set; }

    [Required]
    public required string NewPassword { get; set; }
}

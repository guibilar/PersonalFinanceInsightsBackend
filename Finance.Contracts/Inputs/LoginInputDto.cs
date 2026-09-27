using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class LoginInputDto
{
    [Required]
    public required string Username { get; set; }

    [Required]
    public required string Password { get; set; }

    /// <summary>Keep the session after the browser closes (for up to 14 days of inactivity).</summary>
    public bool RememberMe { get; set; }
}

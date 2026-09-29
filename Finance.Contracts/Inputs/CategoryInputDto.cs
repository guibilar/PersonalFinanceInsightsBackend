using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class CategoryInputDto
{
    /// <summary>Trimmed; unique ignoring case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.CategoryName)]
    public string Name { get; set; } = string.Empty;

    /// <summary>"#RRGGBB" (either case); stored upper-case.</summary>
    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "The colour must look like #1F76B5.")]
    public string Color { get; set; } = string.Empty;
}

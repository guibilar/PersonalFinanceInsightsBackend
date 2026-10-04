using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class TagInputDto
{
    /// <summary>Trimmed, inner spaces collapsed; unique ignoring case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.TagName)]
    public required string Name { get; set; }
}

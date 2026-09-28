using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class CategoryRuleUpdateDto
{
    /// <summary>Matched anywhere in a transaction's description, ignoring case. Stored trimmed and upper-case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.CategoryRuleKeyword)]
    public string Keyword { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(Category))]
    public Category? Category { get; set; }
}

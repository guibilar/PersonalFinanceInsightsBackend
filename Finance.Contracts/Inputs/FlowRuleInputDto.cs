using System.ComponentModel.DataAnnotations;
using Finance.Domain.Enums;

namespace Finance.Contracts.Inputs;

public class FlowRuleInputDto
{
    /// <summary>Matched anywhere in a transaction's description, ignoring case. Stored trimmed and upper-case.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(TextLimits.FlowRuleKeyword)]
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Any flow but Spending, which is what money out is when no rule matches.</summary>
    [Required, EnumDataType(typeof(Flow))]
    public Flow? Flow { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class CategoryRuleInputDto : CategoryRuleUpdateDto
{
    /// <summary>Where to insert the rule (0 is checked first; the rules from there on move down). At the end when omitted.</summary>
    [Range(0, int.MaxValue)]
    public int? Position { get; set; }
}

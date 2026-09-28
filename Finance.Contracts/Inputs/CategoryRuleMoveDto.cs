using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class CategoryRuleMoveDto
{
    /// <summary>The rule's new position (0 is checked first); past the end means last.</summary>
    [Required, Range(0, int.MaxValue)]
    public int? Position { get; set; }
}

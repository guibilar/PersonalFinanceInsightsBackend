namespace Finance.Contracts.Inputs;

/// <summary>A rule to try out before adding it.</summary>
public class CategoryRulePreviewInputDto : CategoryRuleUpdateDto
{
    /// <summary>Left out of the count, e.g. the transaction being edited, whose category the user picks by hand.</summary>
    public Guid? ExcludeTransactionId { get; set; }
}

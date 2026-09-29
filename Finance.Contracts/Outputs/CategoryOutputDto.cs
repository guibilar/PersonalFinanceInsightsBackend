namespace Finance.Contracts.Outputs;

public class CategoryOutputDto : CategoryReferenceDto
{
    /// <summary>Including deleted (hidden) ones: deleting the category moves them all.</summary>
    public int TransactionCount { get; set; }

    public int RuleCount { get; set; }

    /// <summary>Set for the categories the app relies on (Other and Rent): why it can't be deleted. Null for the rest.</summary>
    public string? BuiltInReason { get; set; }
}

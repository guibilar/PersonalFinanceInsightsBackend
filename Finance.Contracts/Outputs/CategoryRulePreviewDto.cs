namespace Finance.Contracts.Outputs;

public class CategoryRulePreviewDto
{
    /// <summary>Stored transactions whose category the rule would change if it were added first and the rules re-applied.</summary>
    public int Count { get; set; }

    /// <summary>Up to five of their descriptions (distinct, most recent first).</summary>
    public List<string> Examples { get; set; } = [];
}

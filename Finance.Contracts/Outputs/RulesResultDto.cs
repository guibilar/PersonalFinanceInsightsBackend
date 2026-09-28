namespace Finance.Contracts.Outputs;

public class RulesResultDto
{
    /// <summary>Transactions the rules were applied to (those with both category and flow picked by hand are skipped).</summary>
    public int Checked { get; set; }

    /// <summary>Transactions whose category or flow changed.</summary>
    public int Updated { get; set; }
}

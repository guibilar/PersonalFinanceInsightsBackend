namespace Finance.Contracts.Outputs;

public class CategoryRulesResultDto
{
    /// <summary>Transactions the rules were applied to (hand-picked categories, savings and fixed rent are skipped).</summary>
    public int Checked { get; set; }
    public int Updated { get; set; }
}

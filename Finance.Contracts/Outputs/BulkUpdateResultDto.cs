namespace Finance.Contracts.Outputs;

public class BulkUpdateResultDto
{
    /// <summary>Transactions that changed.</summary>
    public int Updated { get; set; }

    /// <summary>Transactions left as they were for a reason <see cref="Notes"/> gives: not found, or a flow that doesn't fit.</summary>
    public int Skipped { get; set; }

    public List<string> Notes { get; set; } = [];
}

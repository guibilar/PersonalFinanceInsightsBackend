namespace Finance.Contracts.Outputs;

public class ImportResultDto
{
    public int Read { get; set; }
    public int Added { get; set; }
    /// <summary>Rows already in the database from an earlier import.</summary>
    public int Skipped { get; set; }
}

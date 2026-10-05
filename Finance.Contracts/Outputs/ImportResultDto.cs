namespace Finance.Contracts.Outputs;

public class ImportResultDto
{
    public int Read { get; set; }
    public int Added { get; set; }
    /// <summary>Rows already in the database from an earlier import.</summary>
    public int Skipped { get; set; }

    /// <summary>The account's value at the end of the statement's period, when the statement gives it; it is saved as the account's balance that day.</summary>
    public decimal? Balance { get; set; }

    /// <summary>The day of <see cref="Balance"/>, at midnight UTC.</summary>
    public DateTime? BalanceDate { get; set; }
}

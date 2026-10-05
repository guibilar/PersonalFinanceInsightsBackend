namespace Finance.Contracts.Outputs;

public class AccountBalanceOutputDto
{
    public Guid Id { get; set; }

    /// <summary>The calendar day, at midnight UTC.</summary>
    public DateTime Date { get; set; }

    public decimal Amount { get; set; }
}

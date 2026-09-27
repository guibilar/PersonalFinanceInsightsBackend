namespace Finance.Contracts.Outputs;

public class SignedOutSessionsDto
{
    /// <summary>How many other sessions (browsers, devices) were signed out.</summary>
    public int Count { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

/// <summary>An account's balance at the end of a day, as its bank or broker shows it.</summary>
public class AccountBalanceInputDto
{
    /// <summary>Not in the future; one per day (entering another replaces it).</summary>
    [Required]
    public DateOnly? Date { get; set; }

    /// <summary>For a credit card, what it owes; otherwise what it holds (negative for an overdraft).</summary>
    [Required, Range(-100_000_000, 100_000_000)]
    public decimal? Amount { get; set; }
}

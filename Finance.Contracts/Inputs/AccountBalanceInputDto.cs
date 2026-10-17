using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

/// <summary>An account's balance at the end of a day, as its bank or broker shows it.</summary>
public class AccountBalanceInputDto
{
    /// <summary>Not in the future; one per day (entering another replaces it).</summary>
    [Required]
    public DateOnly? Date { get; set; }

    /// <summary>For a credit card, what it owes; otherwise what it holds (negative for an overdraft).</summary>
    // As a decimal: with whole-number bounds, an amount past what an int holds failed the check with a 500.
    [Required, Range(typeof(decimal), "-100000000", "100000000"), Cents]
    public decimal? Amount { get; set; }
}

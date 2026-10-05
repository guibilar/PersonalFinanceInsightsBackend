namespace Finance.Domain.Entities;

/// <summary>
/// An account's balance at the end of a day, as its bank or broker shows it: what an investment is worth, what a card
/// owes, what a bank account holds. Between two of them the transactions carry it on.
/// </summary>
public class AccountBalance : BaseEntity
{
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>The calendar day, at midnight UTC.</summary>
    public DateTime Date { get; set; }

    /// <summary>For a credit card, what is owed (positive); for the others, what the account holds.</summary>
    public decimal Amount { get; set; }
}

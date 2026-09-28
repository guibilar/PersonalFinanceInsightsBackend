using System.ComponentModel.DataAnnotations;

namespace Finance.Contracts.Inputs;

public class StatementImportDto
{
    /// <summary>Which account the statement belongs to; its kind decides what the rows mean by default.</summary>
    [Required]
    public Guid? AccountId { get; set; }

    /// <summary>
    /// Import even though the file doesn't look like it belongs to the account (a credit card export into a bank
    /// account, or rows already stored on another account). Without it, that is refused with 409 "account-mismatch".
    /// </summary>
    public bool Confirmed { get; set; }
}

using Finance.Domain.Entities;

namespace Finance.Application.Models;

/// <param name="Balance">The latest balance entered, if any.</param>
/// <param name="Deposited">This year's money in counted as transfers (for a registered plan: contributions).</param>
/// <param name="Withdrawn">This year's money out counted as transfers (for a TFSA: room again next year).</param>
/// <param name="LastTransactionDate">The day of its latest transaction (deleted ones included), if it has any.</param>
public record AccountSummary(Account Account, int TransactionCount, AccountBalance? Balance = null, decimal Deposited = 0, decimal Withdrawn = 0,
    DateTime? LastTransactionDate = null);
